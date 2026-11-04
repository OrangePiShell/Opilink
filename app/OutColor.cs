using System;
using System.Drawing;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace OpiLink
{
    // Colors one line of tool output by how important it is. Mode 0 = no colors, 1 = words only (logs, updates), 2 = full (Network, Hardware, Storage).
    static class OutColor
    {
        static readonly Color Normal_ = Color.FromArgb(35, 35, 35);
        public static readonly Color NormalBase = Color.FromArgb(35, 35, 35);
        public static Color Normal { get { return Theme.F(Normal_); } }
        static readonly Color Dim_ = Color.FromArgb(120, 120, 120);
        public static Color Dim { get { return Theme.F(Dim_); } }
        static readonly Color Head_ = Color.FromArgb(194, 84, 10);
        public static Color Head { get { return Theme.F(Head_); } }
        static readonly Color Red_ = Color.FromArgb(200, 35, 35);
        public static Color Red { get { return Theme.F(Red_); } }
        static readonly Color Amber_ = Color.FromArgb(190, 110, 0);
        public static Color Amber { get { return Theme.F(Amber_); } }
        static readonly Color Green_ = Color.FromArgb(25, 135, 60);
        public static Color Green { get { return Theme.F(Green_); } }
        static readonly Color Purple_ = Color.FromArgb(110, 60, 180);
        public static Color Purple { get { return Theme.F(Purple_); } }
        static readonly Color Blue_ = Color.FromArgb(20, 105, 190);
        public static Color Blue { get { return Theme.F(Blue_); } }
        static readonly Color Brown_ = Color.FromArgb(140, 90, 30);
        public static Color Brown { get { return Theme.F(Brown_); } }
        static readonly Color Teal_ = Color.FromArgb(0, 130, 130);
        public static Color Teal { get { return Theme.F(Teal_); } }

        static readonly Regex Pct = new Regex(@"(\d{1,3})%");
        static readonly Regex Temp = new Regex(@"(\d{1,3})(?:\.\d+)?\s*(?:C|Celsius)\b");

        public static Color Level(int v, int amber, int red)
        {
            return v >= red ? Red : v >= amber ? Amber : Green;
        }

        public static Color Temp2(int c) { return Level(c, 65, 80); }

        // Color for a one-line status message (the small text under buttons in popups).
        // Returns Normal-ish grey when the message is neutral.
        public static Color Status(string text, Color neutral)
        {
            string l = (text ?? "").ToLowerInvariant();
            if (l.Length == 0) return neutral;
            string[] red = { "failed", "could not", "not done", "cannot", "did not come back", "error", "denied", "already used", "no board found", "stopped with code" };
            string[] amber = { "still answering", "not sure", "must start", "pick a folder", "a port is a number", "forwarding stopped", "may not have worked" };
            string[] green = { "saved ", "running:", "connected to", "found the board", "the board is off", "the board is back" };
            foreach (string s in red) if (l.Contains(s)) return Red;
            foreach (string s in amber) if (l.Contains(s)) return Amber;
            foreach (string s in green) if (l.StartsWith(s) || l.Contains(s)) return Green;
            if (l.Contains("...") || l.StartsWith("downloading") || l.StartsWith("uploading")) return Blue;
            return neutral;
        }

        public static Color Line(string line, int mode, out bool bold)
        {
            bold = false;
            string t = (line ?? "").Trim();
            if (mode == 0 || t.Length == 0) return Normal;
            if (t.StartsWith("== ") && t.EndsWith(" ==")) { bold = true; return Head; }
            string l = t.ToLowerInvariant();

            if (mode == 2)
            {
                if (t.StartsWith("-- /dev/")) { bold = true; return Blue; }
                if (t.StartsWith("/dev/"))
                {
                    if (t.IndexOf("NVMe SSD", StringComparison.Ordinal) >= 0) { bold = true; return Purple; }
                    if (t.IndexOf("SD card", StringComparison.Ordinal) >= 0) { bold = true; return Teal; }
                    if (t.IndexOf("Hard disk", StringComparison.Ordinal) >= 0) { bold = true; return Brown; }
                    if (t.IndexOf("SSD / flash", StringComparison.Ordinal) >= 0) { bold = true; return Blue; }
                }
                if (t.StartsWith("NAME ") || t.StartsWith("Filesystem ")) return Dim;
            }

            if (l.Contains("failed") || l.Contains("error") || l.Contains("denied") || l.Contains("cannot") || l.Contains("fatal") ||
                l.Contains("was not installed") || l.Contains("no such")) return Red;
            if (l.Contains("passed") || l.EndsWith(": ok") || l.Contains("update finished") || l.Contains("docker is installed")) return Green;
            if (l.Contains("warning") || l.Contains("not installed") || l.Contains("no wi-fi") || l.Contains("not available") ||
                l.Contains("no drives") || l.Contains("no nvme") || l.Contains("not found")) return Amber;

            if (mode == 2)
            {
                Match m = Temp.Match(t);
                if (m.Success && (l.Contains("temp") || l.Contains("thermal") || t.EndsWith(" C") || l.Contains("celsius")))
                    return Level(int.Parse(m.Groups[1].Value), 65, 80);
                m = Pct.Match(t);
                if (m.Success)
                {
                    int v = int.Parse(m.Groups[1].Value);
                    if (l.Contains("spare")) return v >= 50 ? Green : v >= 20 ? Amber : Red; // more is better here
                    return Level(v, 75, 90);
                }
            }
            return Normal;
        }
    }

    // A Label that colors itself from its text: red = failed, amber = warning, green = done / OK, blue = busy.
    class StatusLabel : Label
    {
        public Color Neutral = Color.FromArgb(60, 60, 60);
        EventHandler themeH;
        string en = "";   // the English text; the color is chosen from it, the label shows the translation

        public StatusLabel()
        {
            themeH = delegate { ForeColor = OutColor.Status(en, Theme.F(Neutral)); };
            Theme.Changed += themeH;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= themeH;
            base.Dispose(disposing);
        }

        public override string Text
        {
            get { return base.Text; }
            set { en = value ?? ""; base.Text = L.T(en); ForeColor = OutColor.Status(en, Theme.F(Neutral)); }
        }
    }
}
