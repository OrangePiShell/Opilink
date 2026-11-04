using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading;

namespace OpiLink
{
    // Base for the tool windows: remembers the board and has a safe "run on the UI thread" helper.
    class ToolBase : Form
    {
        protected readonly string ip, user;
        protected volatile bool closing;

        protected ToolBase(string ip, string user, Icon icon, string title, Size size)
        {
            this.ip = ip; this.user = user;
            Font = new Font("Segoe UI", 9.5F);
            Text = title + " - " + user + "@" + ip;
            if (icon != null) Icon = icon;
            BackColor = Color.FromArgb(240, 240, 240);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = size;
        }

        protected void Ui(Action a)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        protected static string OneLine(string s)
        {
            return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // Makes the typical "needs root" failure readable.
        protected static string Hint(string err)
        {
            string e = err ?? "";
            if (e.IndexOf("sudo", StringComparison.OrdinalIgnoreCase) >= 0 && (e.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0))
                return "This needs root. Log in as root, or give this user passwordless sudo on the board.";
            return OneLine(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.Attach(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel) closing = true;
        }
    }

    class Choice
    {
        public string Label, Cmd, Confirm;
        public Choice(string label, string cmd, string confirm) { Label = label; Cmd = cmd; Confirm = confirm; }
        public override string ToString() { return L.T(Label); }
    }

    // Shows the text a command prints, live. Used by Network info, Hardware, Storage, Logs, Update, Run command and the list tools.
    class OutputForm : ToolBase
    {
        static readonly Regex Ansi = new Regex("\u001B\\[[0-9;?]*[A-Za-z]");
        readonly Choice[] choices;
        readonly bool askInput;
        readonly int timeoutMs;
        public string GuardClose;     // when set, closing the window while the command runs asks first
        public bool RunOnPick;        // run again as soon as another item is picked in the list
        RichTextBox txtOut;
        TextBox txtIn;
        public int ColorMode;         // 0 = plain text, 1 = colors for error / ok words, 2 = full colors (Network, Hardware, Storage)
        string partial = "";
        Font mono, monoBold;
        ComboBox cmb;
        RoundButton btnRun, btnStop, btnCopy;
        Label lblStatus;
        Panel top, bottom;
        Control sel;
        readonly StringBuilder pending = new StringBuilder();
        System.Windows.Forms.Timer flush;
        volatile bool running, cancel;

        public OutputForm(string ip, string user, Icon icon, string title, Choice[] choices, bool askInput, string runLabel, bool autoRun, int timeoutMs)
            : base(ip, user, icon, title, new Size(860, 560))
        {
            this.choices = choices; this.askInput = askInput; this.timeoutMs = timeoutMs;
            MinimumSize = new Size(560, 320);
            top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
            bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.White };
            mono = new Font("Consolas", 10F); monoBold = new Font("Consolas", 10F, FontStyle.Bold);
            txtOut = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both, Font = mono, BackColor = Color.White, ForeColor = OutColor.NormalBase, BorderStyle = BorderStyle.None, DetectUrls = false };
            Controls.Add(txtOut); Controls.Add(top); Controls.Add(bottom);
            txtOut.BringToFront();

            btnRun = new RoundButton { Text = runLabel };
            btnRun.Click += delegate { Run(); };
            btnStop = new RoundButton { Text = "Stop", Enabled = false };
            btnStop.Click += delegate
            {
                if (GuardClose != null && running && Msg.Show(this, GuardClose, "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                cancel = true;
            };
            btnCopy = new RoundButton { Text = "Copy" };
            btnCopy.Click += delegate { if (txtOut.TextLength > 0) { try { Clipboard.SetText(txtOut.Text); lblStatus.Text = "Copied."; } catch (Exception) { } } };
            top.Controls.AddRange(new Control[] { btnRun, btnStop, btnCopy });

            if (askInput)
            {
                txtIn = new TextBox { Font = new Font("Consolas", 10F) };
                txtIn.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(); } };
                sel = txtIn;
            }
            else if (choices.Length > 1)
            {
                cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (Choice ch in choices) cmb.Items.Add(ch);
                cmb.SelectedIndex = 0;
                cmb.SelectedIndexChanged += delegate { if (RunOnPick && !running) { Choice c = (Choice)cmb.SelectedItem; if (c.Confirm == null) Run(); } };
                sel = cmb;
            }
            if (sel != null) top.Controls.Add(sel);
            top.Resize += delegate { LayoutBars(); };

            lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), ForeColor = Color.FromArgb(90, 90, 90) };
            bottom.Controls.Add(lblStatus);
            LayoutBars();

            flush = new System.Windows.Forms.Timer { Interval = 150 };
            flush.Tick += delegate { Flush(false); };
            flush.Start();
            EventHandler themeH = delegate { Recolor(); };
            Theme.Changed += themeH;
            FormClosed += delegate { flush.Stop(); flush.Dispose(); Theme.Changed -= themeH; };
            Shown += delegate
            {
                if (askInput) { lblStatus.Text = "Type one command and press Enter. Programs that wait for keys (nano, top) do not work here - use Open terminal."; txtIn.Focus(); }
                if (autoRun) Run();
            };
        }

        void LayoutBars()
        {
            int w = top.ClientSize.Width;
            btnCopy.SetBounds(w - 90, 8, 80, 32);
            btnStop.SetBounds(w - 166, 8, 70, 32);
            btnRun.SetBounds(w - 282, 8, 110, 32);
            if (sel != null) sel.SetBounds(10, 11, Math.Max(100, w - 292), 26);
        }

        // Complete lines get a color by importance; an unfinished last line waits for the next flush.
        void Flush(bool last)
        {
            string s;
            lock (pending)
            {
                if (pending.Length == 0 && !(last && partial.Length > 0)) return;
                s = pending.ToString(); pending.Length = 0;
            }
            s = Ansi.Replace(s, "").Replace("\r", "");
            string[] lines = (partial + s).Split('\n');
            int n = lines.Length - 1;
            partial = lines[n];
            if (txtOut.TextLength > 400000) { txtOut.Select(0, txtOut.TextLength - 250000); txtOut.SelectedText = ""; }
            for (int i = 0; i < n; i++) AddLine(lines[i]);
            if (last && partial.Length > 0) { AddLine(partial); partial = ""; }
            txtOut.SelectionStart = txtOut.TextLength;
            txtOut.ScrollToCaret();
        }

        // The theme changed: write the lines again with colors that fit the new background.
        void Recolor()
        {
            if (txtOut == null || txtOut.TextLength == 0) return;
            string[] ls = txtOut.Lines;
            int n = ls.Length;
            if (n > 0 && ls[n - 1].Length == 0) n--;
            txtOut.Clear();
            for (int i = 0; i < n; i++) AddLine(ls[i]);
            txtOut.SelectionStart = txtOut.TextLength;
            txtOut.ScrollToCaret();
        }

        void AddLine(string line)
        {
            bool bold;
            Color c = OutColor.Line(line, ColorMode, out bold);
            txtOut.SelectionStart = txtOut.TextLength;
            txtOut.SelectionLength = 0;
            txtOut.SelectionColor = c;
            txtOut.SelectionFont = bold ? monoBold : mono;
            txtOut.AppendText(line + "\n");
        }

        void Run()
        {
            if (running) return;
            string cmd, confirm = null;
            if (askInput)
            {
                cmd = txtIn.Text.Trim();
                if (cmd.Length == 0) { lblStatus.Text = "Type a command first."; return; }
            }
            else
            {
                Choice c = cmb != null ? (Choice)cmb.SelectedItem : choices[0];
                cmd = c.Cmd; confirm = c.Confirm;
            }
            if (confirm != null && Msg.Show(this, confirm, "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            running = true; cancel = false;
            lock (pending) pending.Length = 0;
            txtOut.Clear();
            partial = "";
            btnRun.Enabled = false; btnStop.Enabled = true;
            if (sel != null) sel.Enabled = false;
            lblStatus.Text = "Running...";
            string full = Remote.Sh(cmd, true);
            var th = new Thread(delegate ()
            {
                int code = Remote.RunLive(ip, user, full, delegate (string s) { lock (pending) pending.Append(s); }, delegate () { return cancel || closing; }, timeoutMs);
                Ui(delegate { Finished(code); });
            });
            th.IsBackground = true; th.Start();
        }

        void Finished(int code)
        {
            Flush(true);
            running = false;
            btnRun.Enabled = true; btnStop.Enabled = false;
            if (sel != null) sel.Enabled = true;
            string t = txtOut.Text;
            if (code == 0) lblStatus.Text = "Finished " + DateTime.Now.ToString("HH:mm:ss");
            else if (code == -1) lblStatus.Text = "Stopped.";
            else if (code == -2) lblStatus.Text = "Stopped: it took too long.";
            else if (code == -3) lblStatus.Text = "ssh.exe was not found.";
            else if (t.IndexOf("sudo", StringComparison.OrdinalIgnoreCase) >= 0 && t.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                lblStatus.Text = "This needs root. Log in as root, or give this user passwordless sudo on the board.";
            else if (code == 255) lblStatus.Text = "Could not connect to the board.";
            else lblStatus.Text = "Finished with code " + code + " (see the text above).";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running && GuardClose != null && e.CloseReason == CloseReason.UserClosing)
            {
                if (Msg.Show(this, GuardClose, "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
            if (!e.Cancel) cancel = true;
        }
    }
}
