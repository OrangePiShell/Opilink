using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    // Backup: one folder of the board is packed (tar + gzip) on the board and streamed into a file on this PC. Nothing is written on the board.
    class BackupForm : ToolBase
    {
        readonly bool isRoot;
        TextBox txtFolder;
        CheckBox chkRoot;
        RoundButton btnStart, btnCancel;
        BarProgress bar;
        Label lblStatus;
        volatile bool running, cancel;
        Process proc;

        public BackupForm(string ip, string user, Icon icon, bool isRoot)
            : base(ip, user, icon, "Backup", new Size(560, 300))
        {
            this.isRoot = isRoot || user == "root";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            var card = new CardPanel { Left = 14, Top = 14, Width = 532, Height = 150 };
            Controls.Add(card);
            card.Controls.Add(new Label { Left = 16, Top = 10, Width = 500, Height = 40, BackColor = Color.Transparent, ForeColor = Color.FromArgb(90, 90, 90),
                Text = "Saves one folder of the board as a .tar.gz file on this PC. The folder is only read, nothing is changed on the board." });
            card.Controls.Add(new Label { Left = 16, Top = 58, Width = 110, Height = 24, Text = "Folder on board", BackColor = Color.Transparent });
            txtFolder = new TextBox { Left = 130, Top = 55, Width = 386, Font = new Font("Consolas", 10F), Text = this.isRoot && user == "root" ? "/root" : "/home/" + user };
            card.Controls.Add(txtFolder);
            chkRoot = new CheckBox { Left = 16, Top = 96, Width = 500, Height = 40, Text = "Also read files that only root can read (uses sudo; needs passwordless sudo)", Visible = !this.isRoot, BackColor = Color.Transparent };
            card.Controls.Add(chkRoot);
            btnStart = new RoundButton { Left = 14, Top = 178, Width = 190, Height = 34, Text = "Choose file and start" };
            btnStart.Click += delegate { Start(); };
            btnCancel = new RoundButton { Left = 212, Top = 178, Width = 100, Height = 34, Text = "Cancel", Enabled = false };
            btnCancel.Click += delegate { Cancel(); };
            Controls.AddRange(new Control[] { btnStart, btnCancel });
            bar = new BarProgress { Left = 14, Top = 226, Width = 532, Height = 10 };
            Controls.Add(bar);
            lblStatus = new StatusLabel { Left = 14, Top = 244, Width = 532, Height = 50 };
            Controls.Add(lblStatus);
        }

        void Cancel()
        {
            cancel = true;
            Process p = proc;
            if (p != null) { try { p.Kill(); } catch (Exception) { } }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running && e.CloseReason == CloseReason.UserClosing)
            {
                if (Msg.Show(this, "A backup is running. Stop it and delete the unfinished file?", "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { e.Cancel = true; return; }
                Cancel();
            }
            base.OnFormClosing(e);
        }

        void Start()
        {
            if (running) return;
            string path = txtFolder.Text.Trim();
            if (path.Length > 1) path = path.TrimEnd('/');
            if (path.Length == 0 || path[0] != '/') { lblStatus.Text = "The folder must start with /"; return; }
            if (path == "/") { lblStatus.Text = "Pick a folder below /, for example /home or /etc."; return; }
            if (path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0) { lblStatus.Text = "This folder name cannot be used."; return; }
            int i = path.LastIndexOf('/');
            string parent = i <= 0 ? "/" : path.Substring(0, i), name = path.Substring(i + 1);
            string safe = name.Replace(' ', '_');
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string target;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = L.T("Save the backup as");
                dlg.Filter = "Backup (*.tar.gz)|*.tar.gz";
                dlg.FileName = "backup-" + safe + "-" + DateTime.Now.ToString("yyyy-MM-dd") + ".tar.gz";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }
            bool sudo = !isRoot && chkRoot.Checked;
            string cmd = Remote.Sh("[ -d " + Ssh.Q(path) + " ] || { echo 'Not a folder, or no access.' >&2; exit 2; }; " +
                (sudo ? "$S " : "") + "tar -C " + Ssh.Q(parent) + " -czf - -- " + Ssh.Q(name), true);
            running = true; cancel = false;
            btnStart.Enabled = false; btnCancel.Enabled = true; txtFolder.Enabled = false; chkRoot.Enabled = false;
            bar.Marquee = true;
            lblStatus.Text = "Backing up " + path + " ...";
            string tmp = target + ".part";
            var th = new Thread(delegate ()
            {
                string err = null, warn = null; long total = 0;
                try
                {
                    ProcessStartInfo psi = Ssh.Start(ip, user, cmd, false);
                    var errText = new StringBuilder();
                    Process p = Process.Start(psi);
                    proc = p;
                    using (p)
                    {
                        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs ev) { if (ev.Data != null) lock (errText) errText.AppendLine(ev.Data); };
                        p.BeginErrorReadLine();
                        var sw = Stopwatch.StartNew(); long lastTick = 0;
                        using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                        {
                            byte[] buf = new byte[65536]; int n;
                            Stream s = p.StandardOutput.BaseStream;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            {
                                fs.Write(buf, 0, n); total += n;
                                if (sw.ElapsedMilliseconds - lastTick > 300)
                                {
                                    lastTick = sw.ElapsedMilliseconds;
                                    long t2 = total;
                                    Ui(delegate { lblStatus.Text = "Backing up " + path + "   " + FormatSize(t2) + " saved so far"; });
                                }
                            }
                        }
                        p.WaitForExit();
                        string t; lock (errText) t = errText.ToString().Trim();
                        if (cancel) err = "Cancelled.";
                        else if (p.ExitCode == 1) warn = "Some files changed while they were read. The backup was made, but those files may be inconsistent.";
                        else if (p.ExitCode != 0)
                        {
                            string[] lines = t.Split('\n');
                            var first = new StringBuilder();
                            for (int k = 0; k < lines.Length && k < 3; k++) first.Append(lines[k].Trim() + " ");
                            string msg = first.ToString().Trim();
                            if (msg.Length == 0) msg = "ssh stopped with code " + p.ExitCode;
                            if (t.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0 && !isRoot) msg += "  (tick the sudo box to read all files)";
                            err = msg;
                        }
                    }
                    proc = null;
                    if (err == null)
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(tmp, target);
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                if (err != null) { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
                long tt = total;
                Ui(delegate
                {
                    running = false; proc = null;
                    bar.Marquee = false; bar.Value = 0;
                    btnStart.Enabled = true; btnCancel.Enabled = false; txtFolder.Enabled = true; chkRoot.Enabled = true;
                    if (err != null) lblStatus.Text = "Backup failed: " + OneLine(err);
                    else lblStatus.Text = "Saved " + FormatSize(tt) + " to " + target + (warn != null ? "\n" + warn : "");
                });
            });
            th.IsBackground = true; th.Start();
        }

        static string FormatSize(long n)
        {
            if (n < 1024) return n + " B";
            double v = n / 1024.0; string[] u = { "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : "0.0") + " " + u[i];
        }
    }
}
