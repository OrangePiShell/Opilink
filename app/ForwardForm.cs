using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    // Port forwarding: a port on the board shows up on this PC as http://127.0.0.1:PORT. Works only while this window is open.
    class ForwardForm : ToolBase
    {
        TextBox txtBoard, txtLocal;
        RoundButton btnStart, btnStop, btnOpen;
        Label lblStatus;
        Process proc;
        readonly StringBuilder lastErr = new StringBuilder();
        int localPort;
        bool starting;
        System.Windows.Forms.Timer timer;

        public ForwardForm(string ip, string user, Icon icon)
            : base(ip, user, icon, "Port forwarding", new Size(520, 300))
        {
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            var card = new CardPanel { Left = 14, Top = 14, Width = 492, Height = 170 };
            Controls.Add(card);
            card.Controls.Add(new Label { Left = 16, Top = 10, Width = 460, Height = 62, BackColor = Color.Transparent, ForeColor = Color.FromArgb(90, 90, 90),
                Text = "Shows a port of the board on this PC. Example: a web page on board port 8080 opens at http://127.0.0.1:8080 here. Only this PC can use it, and only while this window is open." });
            card.Controls.Add(new Label { Left = 16, Top = 84, Width = 130, Height = 24, Text = "Port on the board", BackColor = Color.Transparent });
            txtBoard = new TextBox { Left = 160, Top = 81, Width = 100, Font = new Font("Segoe UI", 10F), Text = "8080" };
            card.Controls.Add(txtBoard);
            card.Controls.Add(new Label { Left = 16, Top = 122, Width = 130, Height = 24, Text = "Port on this PC", BackColor = Color.Transparent });
            txtLocal = new TextBox { Left = 160, Top = 119, Width = 100, Font = new Font("Segoe UI", 10F), Text = "8080" };
            card.Controls.Add(txtLocal);
            btnStart = new RoundButton { Left = 14, Top = 196, Width = 110, Height = 34, Text = "Start" };
            btnStart.Click += delegate { Start(); };
            btnStop = new RoundButton { Left = 132, Top = 196, Width = 110, Height = 34, Text = "Stop", Enabled = false };
            btnStop.Click += delegate { StopForward("Stopped."); };
            btnOpen = new RoundButton { Left = 250, Top = 196, Width = 150, Height = 34, Text = "Open in browser", Enabled = false };
            btnOpen.Click += delegate { try { Process.Start("http://127.0.0.1:" + localPort); } catch (Exception) { } };
            Controls.AddRange(new Control[] { btnStart, btnStop, btnOpen });
            lblStatus = new StatusLabel { Left = 14, Top = 242, Width = 492, Height = 48 };
            Controls.Add(lblStatus);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                if (proc == null || starting) return;
                bool dead = false;
                try { dead = proc.HasExited; } catch (Exception) { dead = true; }
                if (dead)
                {
                    string e; lock (lastErr) e = lastErr.ToString().Trim();
                    StopForward("Forwarding stopped." + (e.Length > 0 ? " " + OneLine(e) : ""));
                }
            };
            timer.Start();
            FormClosing += delegate { KillProc(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }

        void KillProc()
        {
            Process p = proc; proc = null;
            if (p != null) { try { if (!p.HasExited) p.Kill(); } catch (Exception) { } try { p.Dispose(); } catch (Exception) { } }
        }

        void StopForward(string text)
        {
            KillProc();
            btnStart.Enabled = true; btnStop.Enabled = false; btnOpen.Enabled = false;
            txtBoard.Enabled = true; txtLocal.Enabled = true;
            lblStatus.Text = text;
        }

        static bool Port(string s, out int n)
        {
            return int.TryParse(s.Trim(), out n) && n >= 1 && n <= 65535;
        }

        void Start()
        {
            if (proc != null || starting) return;
            int board, local;
            if (!Port(txtBoard.Text, out board) || !Port(txtLocal.Text, out local)) { lblStatus.Text = "A port is a number from 1 to 65535."; return; }
            try { var l = new TcpListener(IPAddress.Loopback, local); l.Start(); l.Stop(); }
            catch (Exception) { lblStatus.Text = "Port " + local + " is already used on this PC. Pick another one."; return; }
            starting = true; localPort = local;
            btnStart.Enabled = false; txtBoard.Enabled = false; txtLocal.Enabled = false;
            lblStatus.Text = "Starting...";
            lock (lastErr) lastErr.Length = 0;
            var th = new Thread(delegate ()
            {
                Process p = null; string err = null;
                try
                {
                    p = Process.Start(Remote.ForwardInfo(ip, user, local, board));
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs ev) { if (ev.Data != null) lock (lastErr) lastErr.AppendLine(ev.Data); };
                    p.OutputDataReceived += delegate { };
                    p.BeginErrorReadLine(); p.BeginOutputReadLine();
                    for (int i = 0; i < 12 && !p.HasExited; i++) Thread.Sleep(250);
                    if (p.HasExited) { lock (lastErr) err = lastErr.ToString().Trim(); if (err.Length == 0) err = "ssh stopped with code " + p.ExitCode; }
                }
                catch (Exception e) { err = e.Message; }
                Ui(delegate
                {
                    starting = false;
                    if (err != null)
                    {
                        if (p != null) { try { p.Dispose(); } catch (Exception) { } }
                        proc = null;
                        StopForward("Could not start: " + OneLine(err));
                        return;
                    }
                    proc = p;
                    btnStop.Enabled = true; btnOpen.Enabled = true;
                    lblStatus.Text = "Running: http://127.0.0.1:" + local + "  ->  board port " + board + ". Close this window or press Stop to end it.";
                });
            });
            th.IsBackground = true; th.Start();
        }
    }
}
