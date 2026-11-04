using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    // Checks that this PC can log in to the board without a password prompt (key login), and sets it up once if not.
    static class Login
    {
        public static bool Ensure(IWin32Window owner, string ip, string user)
        {
            string bad = Ssh.CheckTarget(ip, user);
            if (bad != null) { Msg.Show(owner, bad, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
            bool keyInstalled = false, forgot = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    Application.UseWaitCursor = true;
                    try { Ssh.Run(ip, user, "echo ok", 15000); }
                    finally { Application.UseWaitCursor = false; }
                    return true;
                }
                catch (SshException e)
                {
                    if (e.Kind == "hostkey" && !forgot)
                    {
                        forgot = true;
                        DialogResult r = Msg.Show(owner,
                            "The board at " + ip + " has a new identity (normal after burning a new card).\n\nForget the old saved key of this IP and continue?",
                            "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (r != DialogResult.Yes) return false;
                        Ssh.ForgetHostKey(ip);
                        continue;
                    }
                    if (e.Kind == "auth" && !keyInstalled)
                    {
                        keyInstalled = true;
                        DialogResult r = Msg.Show(owner,
                            "First time on this board: Orange Pi Link needs a login key (made only for this app).\n\n" +
                            "A black window will open. Type the password of user \"" + user + "\" on the board there (the letters are not shown) and press Enter. " +
                            "The window closes by itself. Your password is not seen or saved by this app.",
                            "Orange Pi Link", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                        if (r != DialogResult.OK) return false;
                        try { Ssh.InstallKey(ip, user); }
                        catch (Exception ex) { Msg.Show(owner, ex.Message, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
                        continue;
                    }
                    string msg = e.Message;
                    if (e.Kind == "auth") msg = "The board did not accept the login. The password may have been wrong. Try again.\n\n" + msg;
                    Msg.Show(owner, msg, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            return false;
        }
    }

    class LinkForm : Form
    {
        static readonly Color Orange = Color.FromArgb(217, 96, 10);
        readonly string baseDir;
        TextBox txtIp, txtUser;
        RoundButton btnFind, btnConnect;
        Label lblState, lblSys;
        ScrollHost tilePanel;
        readonly List<Tile> tiles = new List<Tile>();
        readonly Dictionary<Tile, Func<SysInfo, string>> needs = new Dictionary<Tile, Func<SysInfo, string>>();
        readonly Dictionary<Tile, string> ids = new Dictionary<Tile, string>();
        SysInfo sys;
        string connIp = "", connUser = "";
        Font bold;
        volatile bool closing;

        public LinkForm(string baseDirectory)
        {
            baseDir = baseDirectory;
            Font = new Font("Segoe UI", 9F);
            bold = new Font(Font, FontStyle.Bold);
            Text = "Orange Pi Link  v1.0.0";
            BackColor = Color.FromArgb(240, 240, 240);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            int wa = Screen.PrimaryScreen.WorkingArea.Height;
            ClientSize = new Size(784, Math.Max(470, Math.Min(620, wa - 50)));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
            Controls.Add(header);
            string wm = Path.Combine(Path.Combine(baseDir, "assets"), "wordmark.png");
            PictureBox logo = null;
            Image logoLight = null, logoDark = null;
            if (File.Exists(wm))
            {
                try
                {
                    logoLight = Image.FromFile(wm);
                    logo = new PictureBox { Left = 16, Top = 6, Width = 200, Height = 58, SizeMode = PictureBoxSizeMode.Zoom, Image = logoLight };
                    header.Controls.Add(logo);
                }
                catch (Exception) { }
            }
            // Soft night / normal switch in the top-right corner of the header.
            header.Controls.Add(new ThemeToggle { Left = ClientSize.Width - 16 - 64, Top = 20, Anchor = AnchorStyles.Top | AnchorStyles.Right });
            // Language picker next to the theme switch. The app restarts to apply the new language.
            var cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Left = ClientSize.Width - 16 - 64 - 10 - 110, Top = 22, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font("Segoe UI", 9F) };
            cmbLang.Items.AddRange(L.Names);
            // Owner-drawn so every language shows its flag (flag emoji do not render in WinForms).
            cmbLang.DrawMode = DrawMode.OwnerDrawFixed;
            cmbLang.ItemHeight = 22;
            cmbLang.DropDownWidth = 130;
            cmbLang.DrawItem += delegate (object langSender, DrawItemEventArgs langArgs)
            {
                if (langArgs.Index < 0) return;
                langArgs.DrawBackground();
                Rectangle flagRect = new Rectangle(langArgs.Bounds.Left + 5, langArgs.Bounds.Top + (langArgs.Bounds.Height - 16) / 2, 24, 16);
                Flags.Draw(langArgs.Graphics, L.Codes[langArgs.Index], flagRect);
                Rectangle textRect = new Rectangle(flagRect.Right + 7, langArgs.Bounds.Top, Math.Max(10, langArgs.Bounds.Right - flagRect.Right - 9), langArgs.Bounds.Height);
                TextRenderer.DrawText(langArgs.Graphics, L.Names[langArgs.Index], cmbLang.Font, textRect, langArgs.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                langArgs.DrawFocusRectangle();
            };
            cmbLang.SelectedIndex = L.Index;
            cmbLang.SelectedIndexChanged += delegate { L.Choose(cmbLang.SelectedIndex); };
            header.Controls.Add(cmbLang);
            EventHandler logoH = delegate
            {
                if (logo == null) return;
                try
                {
                    if (Theme.Dark) { if (logoDark == null) logoDark = Theme.LightenDarkPixels(logoLight); logo.Image = logoDark; }
                    else logo.Image = logoLight;
                }
                catch (Exception) { }
            };
            Theme.Changed += logoH;
            FormClosed += delegate { Theme.Changed -= logoH; };
            Load += delegate { Theme.Attach(this); logoH(null, EventArgs.Empty); };

            var card = new CardPanel { Left = 16, Top = 82, Width = 752, Height = 156, Soft = true };
            Controls.Add(card);
            card.Controls.Add(new Label { Left = 16, Top = 16, Width = 80, Height = 24, Text = "Board IP", Font = bold, BackColor = Color.Transparent });
            txtIp = new TextBox { Left = 100, Top = 13, Width = 200, Font = new Font("Segoe UI", 10F) };
            card.Controls.Add(txtIp);
            btnFind = new RoundButton { Left = 312, Top = 11, Width = 240, Height = 30, Text = "Find board on my network" };
            btnFind.Click += delegate { FindBoard(); };
            card.Controls.Add(btnFind);
            card.Controls.Add(new Label { Left = 16, Top = 56, Width = 80, Height = 24, Text = "User", Font = bold, BackColor = Color.Transparent });
            txtUser = new TextBox { Left = 100, Top = 53, Width = 200, Font = new Font("Segoe UI", 10F), Text = "root" };
            card.Controls.Add(txtUser);
            btnConnect = new RoundButton { Left = 312, Top = 51, Width = 240, Height = 30, Text = "Connect and check the board" };
            btnConnect.Click += delegate { if (Connect()) SaveSettings(); };
            card.Controls.Add(btnConnect);
            lblState = new StatusLabel { Left = 16, Top = 92, Width = 720, Height = 36, Neutral = Color.FromArgb(80, 80, 80), Text = "Type the IP of the board, or press \"Find board\".", BackColor = Color.Transparent };
            card.Controls.Add(lblState);
            lblSys = new Label { Left = 16, Top = 130, Width = 720, Height = 20, Text = "", Font = bold, BackColor = Color.Transparent, ForeColor = Color.FromArgb(180, 78, 6) };
            card.Controls.Add(lblSys);

            tilePanel = new ScrollHost { Left = 16, Top = 248, Width = 752 + 4 + ScrollHost.BarW, Height = ClientSize.Height - 256, BackColor = Color.FromArgb(240, 240, 240) };
            tilePanel.Content.BackColor = Color.FromArgb(240, 240, 240);
            Controls.Add(tilePanel);
            AddTile("files", "View board like WSL", "Browse files, download, upload", true, null);
            AddTile("status", "Board status", "Temperature, load, memory, disk", false, null);
            AddTile("terminal", "Open terminal", "A shell on the board", false, null);
            AddTile("power", "Reboot / Shutdown", "Always asks first", false, null);
            AddTile("network", "Network info", "IP, Wi-Fi, DNS, internet test", false, null);
            AddTile("update", "Update packages", "Check or install updates", false, delegate (SysInfo s) { return s.Package != null ? null : "No apt, dnf or pacman found"; });
            AddTile("services", "Services", "Start, stop, restart", false, delegate (SysInfo s) { return s.Tool("systemctl") ? null : "Not available: needs systemd"; });
            AddTile("run", "Run command", "One command, see the output", false, null);
            AddTile("processes", "Processes", "CPU / RAM list, stop a program", false, delegate (SysInfo s) { return s.Tool("ps") ? null : "Not available: ps not found"; });
            AddTile("docker", "Docker", "Containers: start, stop, logs", false, null);
            AddTile("logs", "Logs", "System log, errors, kernel", false, null);
            AddTile("forward", "Port forwarding", "Board port -> this PC", false, null);
            AddTile("config", "System config", "armbian-config / dietpi-config", false, delegate (SysInfo s) { return s.Tool("armbian-config") || s.Tool("dietpi-config") ? null : "Not available: only on Armbian or DietPi"; });
            AddTile("hardware", "Hardware info", "Model, CPU, temperatures, clocks", false, null);
            AddTile("storage", "Storage / NVMe", "Disks, usage, SMART health", false, null);
            AddTile("backup", "Backup", "Save a folder as .tar.gz on this PC", false, null);
            AddTile("cli", "Add the \"opi\" command to cmd / PowerShell", "Then type  opi  in any cmd or PowerShell window to open the board, like  wsl  - even when this app is closed. Nothing is installed on the board.", false, null);
            int rows = (tiles.Count - 1 + 3) / 4 + 1;
            var foot = new Label { Left = 4, Top = rows * 64 + 4, Width = 720, Height = 20, Text = "Login uses a key made only for this app. No password is stored.", ForeColor = Color.FromArgb(110, 110, 110) };
            tilePanel.Content.Controls.Add(foot);
            tilePanel.SetContentHeight(foot.Bottom + 8);

            LoadSettings();
            txtIp.TextChanged += delegate { ResetConnection(); };
            txtUser.TextChanged += delegate { ResetConnection(); };
            FormClosing += delegate { closing = true; SaveSettings(); };
        }

        string SettingsFile()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpiLink"), "settings.txt");
        }

        void LoadSettings()
        {
            try
            {
                string f = SettingsFile();
                if (!File.Exists(f)) return;
                string[] l = File.ReadAllLines(f);
                if (l.Length > 0 && Ssh.CheckTarget(l[0], "root") == null) txtIp.Text = l[0].Trim();
                if (l.Length > 1 && Ssh.CheckTarget("192.168.0.1", l[1]) == null) txtUser.Text = l[1].Trim();
            }
            catch (Exception) { }
        }

        void SaveSettings()
        {
            try
            {
                if (Ssh.CheckTarget(txtIp.Text, txtUser.Text) != null) return;
                string f = SettingsFile();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllLines(f, new string[] { txtIp.Text.Trim(), txtUser.Text.Trim() });
            }
            catch (Exception) { }
        }

        void Ui(Action a)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        void FindBoard()
        {
            btnFind.Enabled = false;
            lblState.Text = "Searching your network...";
            var th = new Thread(delegate ()
            {
                try
                {
                    object r = Finder.Scan("", delegate () { return closing; }, delegate (int p) { Ui(delegate { lblState.Text = "Searching your network... " + p + "%"; }); });
                    var list = r as List<object>;
                    string pick = null, host = ""; int sshCount = 0; bool likely = false;
                    if (list != null)
                    {
                        foreach (object o in list)
                        {
                            var d = (Dictionary<string, object>)o;
                            if ((bool)d["router"] || !(bool)d["ssh"]) continue;
                            sshCount++;
                            bool lk = (bool)d["likely"];
                            if (pick == null || (lk && !likely)) { pick = (string)d["ip"]; host = (string)d["host"]; likely = lk; }
                        }
                    }
                    Ui(delegate
                    {
                        btnFind.Enabled = true;
                        if (pick == null) { lblState.Text = "No board found. Check that it is on and on the same modem, or type the IP."; return; }
                        txtIp.Text = pick;
                        lblState.Text = (likely ? "Found the board: " : "Found a device with SSH (not sure it is the board): ") + pick + (host.Length > 0 ? "  (" + host + ")" : "") +
                            (sshCount > 1 ? "\n" + sshCount + " devices have SSH open; the first one was picked." : "");
                    });
                }
                catch (Exception e)
                {
                    string m = e.Message;
                    Ui(delegate { btnFind.Enabled = true; lblState.Text = "Search failed: " + m; });
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void AddTile(string id, string title, string sub, bool main, Func<SysInfo, string> need)
        {
            int n = tiles.Count;
            var t = new Tile { Left = (n % 4) * 190, Top = (n / 4) * 64, Width = 182, Height = 56, Title = title, Sub = sub, Main = main };
            if (id == "cli") { t.Left = 0; t.Top = ((n + 3) / 4) * 64; t.Width = 752; }
            t.Click += delegate { Act(id); };
            tilePanel.Content.Controls.Add(t);
            tiles.Add(t); ids[t] = id;
            if (need != null) needs[t] = need;
        }

        // Greys out the tiles that do not work on this system (never an error popup).
        void ApplyTiles()
        {
            foreach (Tile t in tiles)
            {
                string note = null;
                Func<SysInfo, string> f;
                if (sys != null && needs.TryGetValue(t, out f)) note = f(sys);
                t.SetAvailable(note == null, note);
                if (ids[t] == "docker") t.SetSub(sys != null && !sys.Tool("docker") ? "Not installed - click to install" : "Containers: start, stop, logs");
                if (ids[t] == "config" && sys != null && !sys.Unknown)
                    t.SetTitle(sys.Tool("armbian-config") ? "Armbian config" : sys.Tool("dietpi-config") ? "DietPi config" : "System config");
            }
        }

        void ResetConnection()
        {
            if (sys == null || closing || IsDisposed) return;
            sys = null; connIp = ""; connUser = "";
            lblSys.Text = "";
            lblState.Text = "Press \"Connect and check the board\" (or any tool).";
            ApplyTiles();
        }

        // Login (key) + one read-only system check. Remembered until the IP or user changes.
        bool Connect()
        {
            string ip = txtIp.Text.Trim(), user = txtUser.Text.Trim();
            if (sys != null && connIp == ip && connUser == user) return true;
            if (!Login.Ensure(this, ip, user)) return false;
            SysInfo s;
            Application.UseWaitCursor = true;
            try { s = SysInfo.Detect(ip, user); }
            catch (Exception) { s = new SysInfo(); s.Unknown = true; }
            finally { Application.UseWaitCursor = false; }
            sys = s; connIp = ip; connUser = user;
            lblState.Text = "Connected to " + user + "@" + ip;
            lblSys.Text = s.Unknown ? "System not detected - all tools stay enabled" : s.Line;
            ApplyTiles();
            return true;
        }

        void Act(string id)
        {
            if (!Connect()) return;
            SaveSettings();
            string ip = connIp, user = connUser;
            try
            {
                switch (id)
                {
                    case "files": new FilesForm(ip, user, Icon).Show(this); break;
                    case "status": new StatusForm(ip, user, Icon).Show(this); break;
                    case "terminal": Ssh.OpenTerminal(ip, user, null); break;
                    case "power": new PowerForm(ip, user, Icon).Show(this); break;
                    case "network": Out(ip, user, "Network info", new Choice[] { new Choice("Network info", Cmds.Network, null) }, "Refresh", 60000, false, 2); break;
                    case "hardware": Out(ip, user, "Hardware info", new Choice[] { new Choice("Hardware info", Cmds.Hardware, null) }, "Refresh", 60000, false, 2); break;
                    case "storage": Out(ip, user, "Storage", new Choice[] { new Choice("Storage", Cmds.Storage, null) }, "Refresh", 90000, false, 2); break;
                    case "run": new OutputForm(ip, user, Icon, "Run command", new Choice[0], true, "Run", false, 120000).Show(this); break;
                    case "update": OpenUpdate(ip, user); break;
                    case "logs": OpenLogs(ip, user); break;
                    case "services": OpenServices(ip, user); break;
                    case "processes": OpenProcesses(ip, user); break;
                    case "docker": if (sys.Tool("docker")) OpenDocker(ip, user); else OfferDockerInstall(ip, user); break;
                    case "cli": Cli.Run(this, ip, user); break;
                    case "forward": new ForwardForm(ip, user, Icon).Show(this); break;
                    case "backup": new BackupForm(ip, user, Icon, sys.Root).Show(this); break;
                    case "config":
                        Remote.OpenTerminalRun(ip, user,
                            "if command -v armbian-config >/dev/null 2>&1; then C=armbian-config; elif command -v dietpi-config >/dev/null 2>&1; then C=dietpi-config; " +
                            "else echo 'No armbian-config or dietpi-config on this board.'; sleep 6; exit 1; fi; " +
                            "if [ \"$(id -u)\" = 0 ]; then $C; else sudo $C; fi");
                        break;
                }
            }
            catch (Exception e) { Msg.Show(this, e.Message, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void Out(string ip, string user, string title, Choice[] ch, string runLabel, int timeout, bool pick, int color)
        {
            var f = new OutputForm(ip, user, Icon, title, ch, false, runLabel, true, timeout);
            f.RunOnPick = pick;
            f.ColorMode = color;
            f.Show(this);
        }

        void OpenUpdate(string ip, string user)
        {
            string pm = sys.Package;
            var f = new OutputForm(ip, user, Icon, "Update packages", new Choice[] {
                new Choice("Check for updates", Cmds.Update(pm, false), null),
                new Choice("Install updates", Cmds.Update(pm, true), "Install all available updates on the board now?\n\nThis can take several minutes. Do not switch the board off while it runs. Some updates need a reboot afterwards.") },
                false, "Run", false, 0);
            f.ColorMode = 1;
            f.GuardClose = "An update is still running. Closing this window stops it half-way, which can leave packages broken.\n\nClose anyway?";
            f.Show(this);
        }

        void OpenLogs(string ip, string user)
        {
            var list = new List<Choice>();
            if (sys.Tool("journalctl"))
            {
                list.Add(new Choice("System log (last 300 lines)", Cmds.LogPre + "$J -n 300 --no-pager", null));
                list.Add(new Choice("Errors since boot", Cmds.LogPre + "$J -b -p err --no-pager", null));
                list.Add(new Choice("SSH logins (last 100)", Cmds.LogPre + "$J -u ssh -u sshd -n 100 --no-pager", null));
                list.Add(new Choice("Follow live (press Stop to end)", Cmds.LogPre + "$J -f -n 50 --no-pager", null));
            }
            list.Add(new Choice("Kernel messages (dmesg)", Cmds.LogPre + "$K | tail -n 300", null));
            Out(ip, user, "Logs", list.ToArray(), "Run", 0, true, 1);
        }

        static bool RiskyService(string n)
        {
            string l = n.ToLowerInvariant();
            return l.StartsWith("ssh") || l.StartsWith("networkmanager") || l.StartsWith("systemd-networkd") || l.StartsWith("wpa_supplicant") || l.StartsWith("dhcpcd") || l.StartsWith("avahi");
        }

        void OpenServices(string ip, string user)
        {
            Func<string, Func<string[], string>> svc = delegate (string verb)
            {
                return delegate (string[] r) { return "$S systemctl " + verb + " " + Ssh.Q(r[0]); };
            };
            Func<string, Func<string[], string>> ask = delegate (string verb)
            {
                return delegate (string[] r)
                {
                    string t = verb.Substring(0, 1).ToUpperInvariant() + verb.Substring(1) + " the service " + r[0] + " on the board?";
                    if (RiskyService(r[0]) && verb != "start") t += "\n\nWarning: this service may keep the board reachable (network or login). Stopping it can cut your connection.";
                    return t;
                };
            };
            var f = new ListForm(ip, user, Icon, "Services", Cmds.ServicesList,
                new string[] { "Service", "State", "Starts at boot" }, new int[] { 380, 180, 160 },
                new ListAction[] {
                    new ListAction("Start", svc("start"), ask("start"), false),
                    new ListAction("Stop", svc("stop"), ask("stop"), false),
                    new ListAction("Restart", svc("restart"), ask("restart"), false),
                    new ListAction("Details", delegate (string[] r) { return "$S systemctl status --no-pager -l " + Ssh.Q(r[0]); }, null, true)
                }, false, 10000);
            f.Keep = delegate (string[] r) { return !r[0].EndsWith("@.service"); };
            f.RowColor = delegate (string[] r)
            {
                if (r[1].StartsWith("failed")) return Color.FromArgb(190, 40, 40);
                if (r[1].StartsWith("active/running")) return Color.FromArgb(30, 130, 60);
                return Color.FromArgb(110, 110, 110);
            };
            f.Show(this);
        }

        void OpenProcesses(string ip, string user)
        {
            Func<string, Func<string[], string>> kill = delegate (string sig)
            {
                return delegate (string[] r)
                {
                    int pid;
                    if (!int.TryParse(r[0], out pid) || pid <= 1) return null;
                    return "$S kill -" + sig + " " + pid;
                };
            };
            Func<string, Func<string[], string>> ask = delegate (string how)
            {
                return delegate (string[] r)
                {
                    string t = how + " process " + r[0] + " (" + (r[5].Length > 60 ? r[5].Substring(0, 60) + "..." : r[5]) + ")?\n\nUnsaved work in that program is lost.";
                    if (r[5].IndexOf("sshd", StringComparison.OrdinalIgnoreCase) >= 0 || r[5].IndexOf("systemd", StringComparison.OrdinalIgnoreCase) >= 0)
                        t += "\n\nWarning: this looks like a system or login process. Stopping it can cut your connection.";
                    return t;
                };
            };
            var f = new ListForm(ip, user, Icon, "Processes", Cmds.ProcessList,
                new string[] { "PID", "User", "CPU %", "Mem %", "Running", "Command" }, new int[] { 70, 100, 70, 70, 100, 460 },
                new ListAction[] {
                    new ListAction("Stop (ask nicely)", kill("TERM"), ask("Stop"), false),
                    new ListAction("Force kill", kill("KILL"), ask("Force kill"), false)
                }, true, 5000);
            f.Show(this);
        }

        void OfferDockerInstall(string ip, string user)
        {
            string pm = sys.Package;
            if (pm == null)
            {
                Msg.Show(this, "Docker is not installed on the board, and this system has no apt, dnf or pacman, so it cannot be installed from here.", "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Msg.Show(this, "Docker is not installed on the board.\n\nInstall it now?\n\nIt is installed with the system package manager (" + pm + "). The board needs internet and it takes a few minutes.",
                "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var f = new OutputForm(ip, user, Icon, "Install Docker", new Choice[] { new Choice("Install Docker", Cmds.InstallDocker(pm), null) }, false, "Install again", true, 0);
            f.ColorMode = 1;
            f.GuardClose = "The installation is still running. Closing this window stops it half-way.\n\nClose anyway?";
            f.FormClosed += delegate { ResetConnection(); };
            f.Show(this);
        }

        void OpenDocker(string ip, string user)
        {
            Func<string, Func<string[], string>> d = delegate (string verb)
            {
                return delegate (string[] r) { return Cmds.DockerPre + "$D " + verb + " " + Ssh.Q(r[0]); };
            };
            var f = new ListForm(ip, user, Icon, "Docker", Cmds.DockerList,
                new string[] { "Container", "Image", "Status", "Ports" }, new int[] { 200, 250, 180, 260 },
                new ListAction[] {
                    new ListAction("Start", d("start"), null, false),
                    new ListAction("Stop", d("stop"), delegate (string[] r) { return "Stop the container " + r[0] + "?"; }, false),
                    new ListAction("Restart", d("restart"), delegate (string[] r) { return "Restart the container " + r[0] + "?"; }, false),
                    new ListAction("Logs", delegate (string[] r) { return Cmds.DockerPre + "$D logs --tail 200 -f " + Ssh.Q(r[0]) + " 2>&1"; }, null, true)
                }, false, 10000);
            f.RowColor = delegate (string[] r) { return r[2].StartsWith("Up") ? Color.FromArgb(30, 130, 60) : Color.FromArgb(110, 110, 110); };
            f.Show(this);
        }
    }
}
