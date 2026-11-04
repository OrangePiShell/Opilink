using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    class StatusForm : Form
    {
        // One short command; prints key=value lines. Nothing is changed on the board.
        const string Cmd =
            "echo host=$(hostname); [ -r /etc/os-release ] && . /etc/os-release; echo os=$PRETTY_NAME; echo kernel=$(uname -r); " +
            "echo up=$(uptime -p); echo cores=$(nproc 2>/dev/null || echo 1); echo load=$(cut -d' ' -f1-3 /proc/loadavg); " +
            "free -m | awk '/Mem:/{print \"mem=\" $3 \" / \" $2 \" MB\"}'; " +
            "echo temp=$(( $(cat /sys/class/thermal/thermal_zone0/temp 2>/dev/null || echo 0) / 1000 )); " +
            "df -h / | awk 'NR==2{print \"disk=\" $3 \" / \" $2 \" (\" $5 \")\"}'; " +
            "echo ip=$(hostname -I | cut -d' ' -f1); " + Cmds.DriveSummary;

        static readonly string[][] Rows = new string[][] {
            new string[] { "host", "Name" }, new string[] { "os", "System" }, new string[] { "kernel", "Kernel" },
            new string[] { "ip", "IP address" }, new string[] { "up", "Uptime" }, new string[] { "load", "Load (1 / 5 / 15 min)" },
            new string[] { "mem", "Memory used" }, new string[] { "temp", "CPU temperature" }, new string[] { "disk", "Disk /" }, new string[] { "drives", "Drives found" }
        };

        readonly string ip, user;
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        Label lblInfo;
        CheckBox chkAuto;
        RoundButton btnRefresh;
        System.Windows.Forms.Timer timer;
        bool busy;
        int cores = 1;

        // Green = fine, amber = watch it, red = risk. Thresholds: temperature 65 / 80 C, memory and disk 75 / 90 %, load per core 0.7 / 1.0.
        Color ValueColor(string key, string v)
        {
            try
            {
                if (key == "temp")
                {
                    int t;
                    if (!int.TryParse(v.Replace("\u00B0C", "").Trim(), out t)) return OutColor.Amber;
                    return OutColor.Temp2(t);
                }
                if (key == "mem")
                {
                    string[] p = v.Replace("MB", "").Split('/');
                    double used = double.Parse(p[0].Trim()), total = double.Parse(p[1].Trim());
                    return total > 0 ? OutColor.Level((int)(used * 100 / total), 75, 90) : OutColor.Normal;
                }
                if (key == "disk")
                {
                    int a = v.IndexOf('('), b = v.IndexOf('%');
                    return (a >= 0 && b > a) ? OutColor.Level(int.Parse(v.Substring(a + 1, b - a - 1)), 75, 90) : OutColor.Normal;
                }
                if (key == "load")
                {
                    double l1 = double.Parse(v.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) / Math.Max(1, cores);
                    return l1 >= 1.0 ? OutColor.Red : l1 >= 0.7 ? OutColor.Amber : OutColor.Green;
                }
                if (key == "ip") return OutColor.Blue;
            }
            catch (Exception) { }
            return OutColor.Normal;
        }

        public StatusForm(string ip, string user, Icon icon)
        {
            this.ip = ip; this.user = user;
            Font = new Font("Segoe UI", 9.5F);
            Text = "Board status - " + user + "@" + ip;
            if (icon != null) Icon = icon;
            BackColor = Color.FromArgb(240, 240, 240);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Load += delegate { Theme.Attach(this); };
            ClientSize = new Size(480, 420);

            var card = new CardPanel { Left = 14, Top = 14, Width = 452, Height = Rows.Length * 30 + 20 };
            Controls.Add(card);
            int y = 10;
            foreach (string[] r in Rows)
            {
                card.Controls.Add(new Label { Left = 16, Top = y, Width = 150, Height = 24, Text = r[1], ForeColor = Color.FromArgb(110, 110, 110) });
                var v = new Label { Left = 170, Top = y, Width = 270, Height = 24, Text = "...", Font = new Font(Font, FontStyle.Bold) };
                card.Controls.Add(v);
                values[r[0]] = v;
                y += 30;
            }
            int bottom = card.Top + card.Height + 12;
            btnRefresh = new RoundButton { Left = 14, Top = bottom, Width = 120, Height = 32, Text = "Refresh" };
            btnRefresh.Click += delegate { Refresh2(); };
            Controls.Add(btnRefresh);
            chkAuto = new CheckBox { Left = 150, Top = bottom + 6, Width = 200, Height = 22, Text = "Refresh every 5 seconds", Checked = true };
            Controls.Add(chkAuto);
            lblInfo = new Label { Left = 14, Top = bottom + 42, Width = 452, Height = 40, ForeColor = Color.FromArgb(110, 110, 110), Text = "" };
            Controls.Add(lblInfo);
            ClientSize = new Size(480, bottom + 86);

            timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += delegate { if (chkAuto.Checked) Refresh2(); };
            Shown += delegate { Refresh2(); timer.Start(); };
            EventHandler themeH = delegate { Refresh2(); };
            Theme.Changed += themeH;
            FormClosed += delegate { timer.Stop(); timer.Dispose(); Theme.Changed -= themeH; };
        }

        void Refresh2()
        {
            if (busy) return;
            busy = true;
            var th = new Thread(delegate ()
            {
                string text = null, err = null;
                try { text = Ssh.Run(ip, user, Remote.Sh(Cmd, false), 15000); }
                catch (Exception e) { err = e.Message; }
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        if (err != null) { lblInfo.Text = err; lblInfo.ForeColor = OutColor.Red; return; }
                        lblInfo.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss"); lblInfo.ForeColor = Color.FromArgb(110, 110, 110);
                        foreach (string line in text.Split('\n'))
                        {
                            int i = line.IndexOf('=');
                            if (i <= 0) continue;
                            string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                            if (k == "cores") { int.TryParse(v, out cores); continue; }
                            Label l;
                            if (!values.TryGetValue(k, out l)) continue;
                            if (k == "temp") v = (v == "0" || v.Length == 0) ? "not available" : v + " \u00B0C";
                            l.Text = v.Length > 0 ? v : "-";
                            l.ForeColor = ValueColor(k, v);
                        }
                    }));
                }
                catch (Exception) { busy = false; }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
