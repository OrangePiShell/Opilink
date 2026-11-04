using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    class Entry
    {
        public string Name;
        public bool IsDir;
        public long Size;
    }

    // File view of the board, like \\wsl$ for WSL: browse folders, download, upload, open a terminal in the current folder.
    // Nothing here deletes, renames or changes permissions on the board. Upload asks before it replaces a file.
    class FilesForm : Form
    {
        readonly string ip, user;
        string path = "/";
        bool busy;
        volatile bool closing;
        TextBox txtPath;
        ListView lv;
        RoundButton btnUp, btnHome, btnRefresh, btnTerm, btnDown, btnPut;
        Label lblStatus;
        BarProgress bar;
        Panel top, bottom;
        readonly List<Entry> entries = new List<Entry>();
        Font bold;

        public FilesForm(string ip, string user, Icon icon)
        {
            this.ip = ip; this.user = user;
            Font = new Font("Segoe UI", 9.5F);
            bold = new Font(Font, FontStyle.Bold);
            Text = "Board files - " + user + "@" + ip;
            if (icon != null) Icon = icon;
            BackColor = Color.FromArgb(240, 240, 240);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 560);
            MinimumSize = new Size(700, 380);
            base.Load += delegate { Theme.Attach(this); };
            EventHandler themeH = delegate { RecolorDirs(); };
            Theme.Changed += themeH;
            FormClosed += delegate { Theme.Changed -= themeH; };

            top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
            bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.White };
            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F) };
            lv.Columns.Add("Name", 450);
            lv.Columns.Add("Size", 100, HorizontalAlignment.Right);
            lv.Columns.Add("Modified", 150);
            lv.DoubleClick += delegate { OpenSelected(); };
            lv.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) OpenSelected(); else if (e.KeyCode == Keys.Back) GoUp(); };
            lv.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(lv);
            Controls.Add(top);
            Controls.Add(bottom);
            lv.BringToFront();

            btnUp = new RoundButton { Width = 56, Height = 32, Text = "Up" };
            btnUp.Click += delegate { GoUp(); };
            btnHome = new RoundButton { Width = 70, Height = 32, Text = "Home" };
            btnHome.Click += delegate { GoHome(); };
            txtPath = new TextBox { Height = 26, Font = new Font("Consolas", 10F) };
            txtPath.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoTo(txtPath.Text.Trim()); } };
            btnRefresh = new RoundButton { Width = 80, Height = 32, Text = "Refresh" };
            btnRefresh.Click += delegate { Load(path); };
            btnTerm = new RoundButton { Width = 130, Height = 32, Text = "Terminal here" };
            btnTerm.Click += delegate { OpenTerm(); };
            top.Controls.AddRange(new Control[] { btnUp, btnHome, txtPath, btnRefresh, btnTerm });
            top.Resize += delegate { LayoutBars(); };

            btnDown = new RoundButton { Left = 10, Top = 11, Width = 170, Height = 34, Text = "Download selected" };
            btnDown.Click += delegate { Download(); };
            btnPut = new RoundButton { Left = 188, Top = 11, Width = 150, Height = 34, Text = "Upload file here" };
            btnPut.Click += delegate { Upload(); };
            bar = new BarProgress { Top = 14, Height = 10 };
            lblStatus = new StatusLabel { Top = 28, Height = 22, AutoSize = false, Neutral = Color.FromArgb(90, 90, 90) };
            bottom.Controls.AddRange(new Control[] { btnDown, btnPut, bar, lblStatus });
            bottom.Resize += delegate { LayoutBars(); };

            LayoutBars();
            UpdateButtons();
            FormClosing += delegate { closing = true; };
            Shown += delegate { Load("/"); };
        }

        void LayoutBars()
        {
            int w = top.ClientSize.Width;
            btnUp.SetBounds(10, 8, 56, 32);
            btnHome.SetBounds(72, 8, 70, 32);
            btnTerm.SetBounds(w - 140, 8, 130, 32);
            btnRefresh.SetBounds(w - 226, 8, 80, 32);
            txtPath.SetBounds(150, 11, Math.Max(100, w - 150 - 234), 26);
            int bw = bottom.ClientSize.Width;
            bar.SetBounds(350, 14, Math.Max(50, bw - 350 - 14), 10);
            lblStatus.SetBounds(350, 28, Math.Max(50, bw - 350 - 14), 22);
        }

        void Ui(Action a)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            bar.Marquee = on;
            if (!on) bar.Value = 0;
            if (text != null) lblStatus.Text = text;
            UpdateButtons();
        }

        // The theme changed: folder names get the orange that fits the new background.
        void RecolorDirs()
        {
            foreach (ListViewItem it in lv.Items)
            {
                Entry en = it.Tag as Entry;
                if (en != null && en.IsDir) it.ForeColor = Theme.F(Color.FromArgb(180, 78, 6));
            }
        }

        void UpdateButtons()
        {
            bool sel = lv.SelectedItems.Count > 0;
            btnUp.Enabled = !busy && path != "/";
            btnHome.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            btnTerm.Enabled = !busy;
            btnDown.Enabled = !busy && sel;
            btnPut.Enabled = !busy;
            txtPath.Enabled = !busy;
        }

        static string Join(string dir, string name)
        {
            return dir == "/" ? "/" + name : dir.TrimEnd('/') + "/" + name;
        }

        static string FormatSize(long n)
        {
            if (n < 1024) return n + " B";
            double v = n / 1024.0; string[] u = { "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : "0.0") + " " + u[i];
        }

        void GoTo(string p)
        {
            if (p.Length == 0 || p[0] != '/') { lblStatus.Text = "The path must start with /"; return; }
            if (p.Length > 1) p = p.TrimEnd('/');
            if (p.Length == 0) p = "/";
            Load(p);
        }

        void GoUp()
        {
            if (busy || path == "/") return;
            int i = path.LastIndexOf('/');
            Load(i <= 0 ? "/" : path.Substring(0, i));
        }

        void GoHome()
        {
            if (busy) return;
            SetBusy(true, "Looking for the home folder...");
            var th = new Thread(delegate ()
            {
                string home = null, err = null;
                try { home = Ssh.Run(ip, user, "cd; pwd", 15000).Trim(); }
                catch (Exception e) { err = e.Message; }
                Ui(delegate
                {
                    SetBusy(false, err);
                    if (home != null && home.StartsWith("/")) Load(home);
                });
            });
            th.IsBackground = true; th.Start();
        }

        Entry Selected()
        {
            return lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as Entry : null;
        }

        void OpenSelected()
        {
            Entry e = Selected();
            if (e == null || busy) return;
            if (e.IsDir) Load(Join(path, e.Name));
            else Download();
        }

        void Load(string p)
        {
            if (busy) return;
            SetBusy(true, "Loading " + p + " ...");
            var th = new Thread(delegate ()
            {
                string text = null, err = null;
                try
                {
                    string cmd = "[ -d " + Ssh.Q(p) + " ] || { echo Not a folder or no access >&2; exit 2; }; " +
                        "find " + Ssh.Q(p) + " -mindepth 1 -maxdepth 1 -printf '%Y\\t%s\\t%TY-%Tm-%Td %TH:%TM\\t%f\\n'; true";
                    text = Ssh.Run(ip, user, cmd, 30000);
                }
                catch (Exception e) { err = e.Message; }
                var list = new List<KeyValuePair<Entry, string>>();
                if (text != null)
                {
                    foreach (string raw in text.Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        string[] f = line.Split(new char[] { '\t' }, 4);
                        if (f.Length < 4 || f[3].Length == 0) continue;
                        long size; long.TryParse(f[1], out size);
                        list.Add(new KeyValuePair<Entry, string>(new Entry { Name = f[3], IsDir = f[0] == "d", Size = size }, f[2]));
                    }
                    list.Sort(delegate (KeyValuePair<Entry, string> a, KeyValuePair<Entry, string> b)
                    {
                        if (a.Key.IsDir != b.Key.IsDir) return a.Key.IsDir ? -1 : 1;
                        return string.Compare(a.Key.Name, b.Key.Name, StringComparison.OrdinalIgnoreCase);
                    });
                }
                Ui(delegate
                {
                    SetBusy(false, null);
                    if (err != null) { lblStatus.Text = err.Replace("\r", " ").Replace("\n", " "); return; }
                    path = p;
                    txtPath.Text = p;
                    entries.Clear();
                    lv.BeginUpdate();
                    lv.Items.Clear();
                    foreach (var kv in list)
                    {
                        Entry en = kv.Key;
                        entries.Add(en);
                        var it = new ListViewItem(en.IsDir ? en.Name + "/" : en.Name);
                        it.SubItems.Add(en.IsDir ? "" : FormatSize(en.Size));
                        it.SubItems.Add(kv.Value);
                        it.Tag = en;
                        if (en.IsDir) { it.Font = bold; it.ForeColor = Theme.F(Color.FromArgb(180, 78, 6)); }
                        lv.Items.Add(it);
                    }
                    lv.EndUpdate();
                    lblStatus.Text = list.Count + " items";
                    UpdateButtons();
                });
            });
            th.IsBackground = true; th.Start();
        }

        void OpenTerm()
        {
            try { Ssh.OpenTerminal(ip, user, path); }
            catch (Exception e) { Msg.Show(this, e.Message, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void Download()
        {
            Entry e = Selected();
            if (e == null || busy) return;
            string target;
            using (var dlg = new SaveFileDialog())
            {
                dlg.FileName = e.IsDir ? e.Name + ".tar" : e.Name;
                dlg.Title = L.T(e.IsDir ? "Save the folder as a .tar file" : "Save file");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }
            string cmd = e.IsDir ? "tar -C " + Ssh.Q(path) + " -cf - -- " + Ssh.Q(e.Name) : "cat -- " + Ssh.Q(Join(path, e.Name));
            long expected = e.IsDir ? 0 : e.Size;
            string name = e.Name;
            SetBusy(true, "Downloading " + name + " ...");
            if (expected > 0) bar.Marquee = false;
            var th = new Thread(delegate ()
            {
                string err = null; long total = 0;
                string tmp = target + ".part";
                try
                {
                    using (Process p = Process.Start(Ssh.Start(ip, user, cmd, false)))
                    {
                        var errText = new StringBuilder();
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
                                if (sw.ElapsedMilliseconds - lastTick > 200)
                                {
                                    lastTick = sw.ElapsedMilliseconds;
                                    long t2 = total;
                                    Ui(delegate
                                    {
                                        lblStatus.Text = "Downloading " + name + "  " + FormatSize(t2) + (expected > 0 ? " of " + FormatSize(expected) : "");
                                        if (expected > 0) bar.Value = t2 * 100.0 / expected;
                                    });
                                }
                            }
                        }
                        p.WaitForExit();
                        if (p.ExitCode != 0) { string t; lock (errText) t = errText.ToString().Trim(); throw new Exception(t.Length > 0 ? t : "ssh stopped with code " + p.ExitCode); }
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
                catch (Exception ex) { err = ex.Message; try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
                long tt = total;
                Ui(delegate
                {
                    SetBusy(false, err != null ? "Download failed: " + err.Replace("\r", " ").Replace("\n", " ") : "Saved " + FormatSize(tt) + " to " + target);
                });
            });
            th.IsBackground = true; th.Start();
        }

        void Upload()
        {
            if (busy) return;
            string src;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = L.T("Choose a file to send to " + path);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                src = dlg.FileName;
            }
            string name = Path.GetFileName(src);
            if (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0) { lblStatus.Text = "This file name cannot be sent."; return; }
            foreach (Entry en in entries)
            {
                if (en.Name != name) continue;
                string what = en.IsDir ? "A folder" : "A file";
                if (en.IsDir) { Msg.Show(this, "A folder named \"" + name + "\" already exists here.", "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (Msg.Show(this, what + " named \"" + name + "\" is already on the board in " + path + ".\n\nReplace it?", "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                break;
            }
            string full = Join(path, name), tmpRemote = full + ".opilink-part";
            long size = new FileInfo(src).Length;
            // The temp file replaces the target only if it has exactly the size of the source file (a cut or empty upload never replaces anything).
            string cmd = "cat > " + Ssh.Q(tmpRemote)
                + " && [ \"$(wc -c < " + Ssh.Q(tmpRemote) + ")\" -eq " + size + " ]"
                + " && mv -f " + Ssh.Q(tmpRemote) + " " + Ssh.Q(full)
                + " || { rm -f " + Ssh.Q(tmpRemote) + "; exit 1; }";
            SetBusy(true, "Uploading " + name + " ...");
            if (size > 0) bar.Marquee = false;
            string folder = path;
            var th = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    using (Process p = Process.Start(Ssh.Start(ip, user, cmd, true)))
                    {
                        var errText = new StringBuilder();
                        p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs ev) { if (ev.Data != null) lock (errText) errText.AppendLine(ev.Data); };
                        p.OutputDataReceived += delegate { };
                        p.BeginErrorReadLine();
                        p.BeginOutputReadLine();
                        long sent = 0; var sw = Stopwatch.StartNew(); long lastTick = 0;
                        try
                        {
                            using (FileStream fs = File.OpenRead(src))
                            {
                                Stream o = p.StandardInput.BaseStream;
                                byte[] buf = new byte[65536]; int n;
                                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                                {
                                    o.Write(buf, 0, n); sent += n;
                                    if (sw.ElapsedMilliseconds - lastTick > 200)
                                    {
                                        lastTick = sw.ElapsedMilliseconds;
                                        long s2 = sent;
                                        Ui(delegate { lblStatus.Text = "Uploading " + name + "  " + FormatSize(s2) + " of " + FormatSize(size); bar.Value = s2 * 100.0 / size; });
                                    }
                                }
                                o.Flush();
                            }
                        }
                        catch (IOException) { }
                        finally { try { p.StandardInput.Close(); } catch (Exception) { } }
                        p.WaitForExit();
                        if (p.ExitCode != 0) { string t; lock (errText) t = errText.ToString().Trim(); throw new Exception(t.Length > 0 ? t : "ssh stopped with code " + p.ExitCode); }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    SetBusy(false, err != null ? "Upload failed: " + err.Replace("\r", " ").Replace("\n", " ") : "Sent " + name + " to " + folder);
                    if (err == null && folder == path) Load(path);
                });
            });
            th.IsBackground = true; th.Start();
        }
    }
}
