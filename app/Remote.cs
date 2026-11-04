using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OpiLink
{
    // Helpers for running longer or live commands on the board. Nothing here installs anything on the board.
    static class Remote
    {
        // Non-interactive ssh often has a short PATH (no /usr/sbin), so add the usual folders.
        const string PathFix = "export PATH=\"$PATH:/usr/sbin:/sbin:/usr/local/sbin:/usr/local/bin\"; ";
        // $S = "" when we are root, "sudo -n" otherwise (sudo -n never asks for a password; it just fails).
        const string SudoVar = "S=; [ \"$(id -u)\" = 0 ] || S='sudo -n'; ";

        // Wraps a script so it runs in plain sh with the PATH fix (and $S) on any board.
        public static string Sh(string script, bool withSudo)
        {
            return "sh -c " + Ssh.Q(PathFix + (withSudo ? SudoVar : "") + script);
        }

        // Runs a command and sends its text (output and errors) to onText as it arrives.
        // Returns the exit code, -1 = stopped by cancel(), -2 = took longer than timeoutMs (0 = no limit), -3 = ssh.exe missing.
        public static int RunLive(string ip, string user, string remoteCmd, Action<string> onText, Func<bool> cancel, int timeoutMs)
        {
            ProcessStartInfo psi = Ssh.Start(ip, user, remoteCmd, false);
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception)
            {
                onText("ssh.exe was not found. Turn on the Windows feature \"OpenSSH Client\" (Settings > Apps > Optional features).\n");
                return -3;
            }
            using (p)
            {
                Thread t1 = Pump(p.StandardOutput, onText), t2 = Pump(p.StandardError, onText);
                Stopwatch sw = Stopwatch.StartNew();
                while (!p.WaitForExit(200))
                {
                    bool stop = cancel != null && cancel();
                    bool slow = timeoutMs > 0 && sw.ElapsedMilliseconds > timeoutMs;
                    if (stop || slow)
                    {
                        try { p.Kill(); } catch (Exception) { }
                        t1.Join(1000); t2.Join(1000);
                        if (slow && !stop) onText("\n[Stopped: it took too long]\n");
                        return stop ? -1 : -2;
                    }
                }
                p.WaitForExit();
                t1.Join(3000); t2.Join(3000);
                return p.ExitCode;
            }
        }

        static Thread Pump(StreamReader r, Action<string> onText)
        {
            var t = new Thread(delegate ()
            {
                try
                {
                    char[] buf = new char[4096]; int n;
                    while ((n = r.Read(buf, 0, buf.Length)) > 0) onText(new string(buf, 0, n));
                }
                catch (Exception) { }
            });
            t.IsBackground = true; t.Start();
            return t;
        }

        // A normal console window that runs one program on the board (for tools that need a real terminal).
        public static void OpenTerminalRun(string ip, string user, string script)
        {
            string remote = "sh -c " + Ssh.Q("export PATH=\"$PATH:/usr/sbin:/sbin:/usr/local/sbin:/usr/local/bin\"; " + script);
            string args = Ssh.BaseArgs(ip, user, false, true) + " " + Ssh.Arg(remote);
            Process.Start(new ProcessStartInfo("ssh", args) { UseShellExecute = false, CreateNoWindow = false });
        }

        // ssh -L: Windows 127.0.0.1:localPort -> board 127.0.0.1:boardPort. Only this PC can use it.
        public static ProcessStartInfo ForwardInfo(string ip, string user, int localPort, int boardPort)
        {
            string args = "-N -L 127.0.0.1:" + localPort + ":127.0.0.1:" + boardPort + " -o ExitOnForwardFailure=yes " + Ssh.BaseArgs(ip, user, true, false);
            return new ProcessStartInfo("ssh", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
        }

        public static bool PortOpen(string ip, int port, int ms)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    IAsyncResult r = c.BeginConnect(ip, port, null, null);
                    if (!r.AsyncWaitHandle.WaitOne(ms)) return false;
                    c.EndConnect(r);
                    return c.Connected;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
