using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    class ListAction
    {
        public string Label;
        public Func<string[], string> Cmd;      // the script to run for the selected row (null = cannot do it for this row)
        public Func<string[], string> Confirm;  // text of the confirm question (null = no question)
        public bool Live;                       // true = show the output in its own window (logs), false = run, then reload the list
        public ListAction(string label, Func<string[], string> cmd, Func<string[], string> confirm, bool live)
        {
            Label = label; Cmd = cmd; Confirm = confirm; Live = live;
        }
    }

    // One window for Services, Processes and Docker: a list read from the board (tab separated lines), a filter box and action buttons.
    class ListForm : ToolBase
    {
        readonly string listCmd;
        readonly string[] headers;
        readonly ListAction[] actions;
        public Func<string[], bool> Keep;        // optional: false hides a row
        public Func<string[], Color> RowColor;   // optional: Color.Empty = default
        readonly List<string[]> rows = new List<string[]>();
        ListView lv;
        TextBox txtFilter;
        RoundButton btnRefresh;
        RoundButton[] btns;
        CheckBox chkAuto;
        Label lblStatus;
        Panel top, bottom;
        System.Windows.Forms.Timer timer;
        bool busy;

        public ListForm(string ip, string user, Icon icon, string title, string listCmd, string[] headers, int[] widths, ListAction[] actions, bool autoRefresh, int autoMs)
            : base(ip, user, icon, title, new Size(900, 560))
        {
            this.listCmd = listCmd; this.headers = headers; this.actions = actions;
            MinimumSize = new Size(700, 380);
            top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
            bottom = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Color.White };
            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F) };
            for (int i = 0; i < headers.Length; i++) lv.Columns.Add(headers[i], widths[i]);
            lv.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(lv); Controls.Add(top); Controls.Add(bottom);
            lv.BringToFront();

            top.Controls.Add(new Label { Left = 10, Top = 15, Width = 45, Height = 22, Text = "Filter", BackColor = Color.Transparent });
            txtFilter = new TextBox { Left = 58, Top = 11, Width = 240, Font = new Font("Segoe UI", 10F) };
            txtFilter.TextChanged += delegate { Fill(); };
            top.Controls.Add(txtFilter);
            btnRefresh = new RoundButton { Width = 80, Height = 32, Text = "Refresh" };
            btnRefresh.Click += delegate { Reload(); };
            chkAuto = new CheckBox { Width = 170, Height = 22, Text = "Refresh every " + (autoMs / 1000) + " s", Checked = autoRefresh };
            top.Controls.AddRange(new Control[] { btnRefresh, chkAuto });
            top.Resize += delegate { LayoutBars(); };

            btns = new RoundButton[actions.Length];
            int x = 10;
            for (int i = 0; i < actions.Length; i++)
            {
                ListAction a = actions[i];
                var b = new RoundButton { Left = x, Top = 8, Width = 130, Height = 34, Text = a.Label };
                b.Click += delegate { Do(a); };
                btns[i] = b; bottom.Controls.Add(b);
                x += 138;
            }
            lblStatus = new Label { Left = 10, Top = 46, Height = 20, Width = 600, ForeColor = Color.FromArgb(90, 90, 90), BackColor = Color.Transparent };
            bottom.Controls.Add(lblStatus);
            bottom.Resize += delegate { lblStatus.Width = Math.Max(100, bottom.ClientSize.Width - 20); };
            LayoutBars();
            UpdateButtons();

            timer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, autoMs) };
            timer.Tick += delegate { if (chkAuto.Checked && !busy) Reload(); };
            Shown += delegate { Reload(); timer.Start(); };
            EventHandler themeH = delegate { if (rows != null) Fill(); };
            Theme.Changed += themeH;
            FormClosed += delegate { timer.Stop(); timer.Dispose(); Theme.Changed -= themeH; };
        }

        void LayoutBars()
        {
            int w = top.ClientSize.Width;
            btnRefresh.SetBounds(w - 90, 8, 80, 32);
            chkAuto.SetBounds(w - 270, 13, 170, 22);
        }

        string[] Selected()
        {
            return lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as string[] : null;
        }

        void UpdateButtons()
        {
            bool sel = lv.SelectedItems.Count > 0;
            btnRefresh.Enabled = !busy;
            for (int i = 0; i < btns.Length; i++) btns[i].Enabled = !busy && sel;
        }

        void Reload()
        {
            if (busy || closing) return;
            busy = true;
            lblStatus.Text = "Loading...";
            UpdateButtons();
            var th = new Thread(delegate ()
            {
                string text = null, err = null;
                try { text = Ssh.Run(ip, user, Remote.Sh(listCmd, true), 30000); }
                catch (Exception e) { err = e.Message; }
                var list = new List<string[]>();
                if (text != null)
                {
                    foreach (string raw in text.Split('\n'))
                    {
                        string line = raw.TrimEnd('\r');
                        if (line.Trim().Length == 0) continue;
                        string[] f = line.Split(new char[] { '\t' }, headers.Length);
                        if (f.Length < headers.Length)
                        {
                            string[] g = new string[headers.Length];
                            for (int i = 0; i < g.Length; i++) g[i] = i < f.Length ? f[i] : "";
                            f = g;
                        }
                        if (Keep != null && !Keep(f)) continue;
                        list.Add(f);
                    }
                }
                Ui(delegate
                {
                    busy = false;
                    if (err != null) { lblStatus.Text = Hint(err); UpdateButtons(); return; }
                    rows.Clear(); rows.AddRange(list);
                    Fill();
                    lblStatus.Text = rows.Count + " items - updated " + DateTime.Now.ToString("HH:mm:ss");
                    UpdateButtons();
                });
            });
            th.IsBackground = true; th.Start();
        }

        void Fill()
        {
            string[] cur = Selected();
            string key = cur != null ? cur[0] : null;
            string f = txtFilter.Text.Trim().ToLowerInvariant();
            int topIdx = lv.TopItem != null ? lv.TopItem.Index : 0;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (string[] r in rows)
            {
                if (f.Length > 0 && string.Join(" ", r).ToLowerInvariant().IndexOf(f) < 0) continue;
                var it = new ListViewItem(r[0]);
                for (int i = 1; i < r.Length; i++) it.SubItems.Add(r[i]);
                it.Tag = r;
                if (RowColor != null) { Color c = RowColor(r); if (!c.IsEmpty) it.ForeColor = Theme.F(c); }
                lv.Items.Add(it);
                if (key != null && r[0] == key) it.Selected = true;
            }
            if (lv.Items.Count > topIdx && topIdx > 0) { try { lv.TopItem = lv.Items[topIdx]; } catch (Exception) { } }
            lv.EndUpdate();
            UpdateButtons();
        }

        void Do(ListAction a)
        {
            if (busy) return;
            string[] row = Selected();
            if (row == null) return;
            string cmd = a.Cmd(row);
            if (cmd == null) { lblStatus.Text = "This cannot be done for the selected row."; return; }
            string conf = a.Confirm != null ? a.Confirm(row) : null;
            if (conf != null && Msg.Show(this, conf, "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (a.Live)
            {
                var f = new OutputForm(ip, user, Icon, a.Label + " " + row[0], new Choice[] { new Choice(a.Label, cmd, null) }, false, "Run again", true, 0);
                f.ColorMode = 1;
                f.Show(this);
                return;
            }
            busy = true;
            string what = a.Label + " " + row[0];
            lblStatus.Text = what + " ...";
            UpdateButtons();
            var th = new Thread(delegate ()
            {
                string err = null;
                try { Ssh.Run(ip, user, Remote.Sh(cmd, true), 60000); }
                catch (Exception e) { err = e.Message; }
                Ui(delegate
                {
                    busy = false;
                    lblStatus.Text = err != null ? what + " failed: " + Hint(err) : what + " done.";
                    UpdateButtons();
                    Reload();
                    if (err != null)
                    {
                        lblStatus.Text = what + " failed: " + Hint(err);
                        Msg.Show(this, what + " failed:\n\n" + Hint(err), "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                });
            });
            th.IsBackground = true; th.Start();
        }
    }
}
