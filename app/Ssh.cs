using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpiLink
{
    // All talking to the board goes through the Windows OpenSSH client (ssh.exe), the same one the burner uses.
    // No password is ever read, stored or sent by this app: login uses a key made just for this app,
    // installed on the board once (you type the board password in the ssh window, not in this app).
    static class Ssh
    {
        static readonly Regex UserOk = new Regex("^[a-z_][a-z0-9_-]{0,31}$");

        public static string KeyPath
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"), "opilink_ed25519"); }
        }

        public static string CheckTarget(string ip, string user)
        {
            IPAddress a;
            if (!IPAddress.TryParse((ip ?? "").Trim(), out a) || a.AddressFamily != AddressFamily.InterNetwork)
                return "Type the IP address of the board, for example 192.168.1.6";
            byte[] b = a.GetAddressBytes();
            bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            if (!priv) return "Only an address on your own network can be used (10.x, 172.16-31.x, 192.168.x)";
            if (user == null || !UserOk.IsMatch(user.Trim())) return "The user name can only have lowercase letters, digits, _ and -";
            return null;
        }

        // 'text' with single quotes made safe for the board's shell.
        public static string Q(string s)
        {
            return "'" + s.Replace("'", "'\\''") + "'";
        }

        // One argument for a Windows command line.
        public static string Arg(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in s)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string BaseArgs(string ip, string user, bool batch, bool tty)
        {
            var sb = new StringBuilder();
            if (tty) sb.Append("-t ");
            sb.Append("-i " + Arg(KeyPath) + " -o IdentitiesOnly=yes");
            if (batch) sb.Append(" -o BatchMode=yes");
            sb.Append(" -o ConnectTimeout=6 -o ServerAliveInterval=15 -o StrictHostKeyChecking=accept-new ");
            sb.Append(user.Trim() + "@" + ip.Trim());
            return sb.ToString();
        }

        public static ProcessStartInfo Start(string ip, string user, string remoteCmd, bool redirectIn)
        {
            string args = BaseArgs(ip, user, true, false) + (remoteCmd != null ? " " + Arg(remoteCmd) : "");
            return new ProcessStartInfo("ssh", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = redirectIn
            };
        }

        // Runs one command on the board and returns its text. Throws SshException with the ssh error text.
        public static string Run(string ip, string user, string remoteCmd, int timeoutMs)
        {
            ProcessStartInfo psi = Start(ip, user, remoteCmd, false);
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception e) { throw new SshException("ssh.exe was not found. Turn on the Windows feature \"OpenSSH Client\" (Settings > Apps > Optional features).", "nossh", e); }
            using (p)
            {
                var err = new StringBuilder();
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs ev) { if (ev.Data != null) lock (err) err.AppendLine(ev.Data); };
                p.BeginErrorReadLine();
                string outText = null;
                var t = new Thread(delegate () { outText = p.StandardOutput.ReadToEnd(); });
                t.IsBackground = true; t.Start();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                    throw new SshException("The board did not answer in time.", "timeout", null);
                }
                t.Join(3000);
                p.WaitForExit();
                string e2; lock (err) e2 = err.ToString().Trim();
                if (p.ExitCode != 0) throw new SshException(e2.Length > 0 ? e2 : ("ssh stopped with code " + p.ExitCode), Classify(e2), null);
                return outText ?? "";
            }
        }

        static string Classify(string err)
        {
            if (err.IndexOf("HOST IDENTIFICATION HAS CHANGED", StringComparison.OrdinalIgnoreCase) >= 0) return "hostkey";
            if (err.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0) return "auth";
            return "other";
        }

        public static bool HasKey() { return File.Exists(KeyPath) && File.Exists(KeyPath + ".pub"); }

        public static void MakeKey()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath));
            if (HasKey()) return;
            var psi = new ProcessStartInfo("ssh-keygen", "-q -t ed25519 -N \"\" -C opilink -f " + Arg(KeyPath))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                if (!HasKey()) throw new InvalidOperationException("Could not make the key: " + (e + o).Trim());
            }
        }

        // One-time step: a console window opens, you type the board password there, the key is copied to the board.
        public static void InstallKey(string ip, string user)
        {
            MakeKey();
            string args = "/c type " + Arg(KeyPath + ".pub") + " | ssh -o StrictHostKeyChecking=accept-new " + user.Trim() + "@" + ip.Trim()
                + " \"umask 077; mkdir -p ~/.ssh; cat >> ~/.ssh/authorized_keys\"";
            var psi = new ProcessStartInfo("cmd.exe", args) { UseShellExecute = true };
            using (Process p = Process.Start(psi)) { p.WaitForExit(); }
        }

        public static void ForgetHostKey(string ip)
        {
            try
            {
                var kg = new ProcessStartInfo("ssh-keygen", "-R " + ip.Trim()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(kg)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(5000); }
            }
            catch (Exception) { }
        }

        // A normal console window with a shell on the board, started in the given folder (like typing "wsl" in a folder).
        public static void OpenTerminal(string ip, string user, string dir)
        {
            string remote = dir == null ? null : "cd " + Q(dir) + " 2>/dev/null; exec \"${SHELL:-bash}\" -l";
            string args = BaseArgs(ip, user, false, true) + (remote != null ? " " + Arg(remote) : "");
            Process.Start(new ProcessStartInfo("ssh", args) { UseShellExecute = false, CreateNoWindow = false });
        }
    }

    class SshException : Exception
    {
        public string Kind;
        public SshException(string msg, string kind, Exception inner) : base(msg, inner) { Kind = kind; }
    }
}
