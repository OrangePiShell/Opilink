using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpiLink
{
    // Night / normal theme. Normal is the default. The choice is remembered in %APPDATA%\OpiLink\theme.txt.
    // Forms call Theme.Attach(this) once; after that every control is re-colored when the theme changes.
    static class Theme
    {
        public static bool Dark;
        public static event EventHandler Changed;

        static readonly ConditionalWeakTable<Control, Orig> origs = new ConditionalWeakTable<Control, Orig>();
        static readonly ConditionalWeakTable<Control, object> nativeDark = new ConditionalWeakTable<Control, object>();
        static readonly ConditionalWeakTable<Control, object> hooked = new ConditionalWeakTable<Control, object>();

        class Orig { public bool BackSet, ForeSet; public Color Back, Fore; }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        static string FilePath()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpiLink"), "theme.txt");
        }

        public static void Load()
        {
            try
            {
                string f = FilePath();
                Dark = File.Exists(f) && File.ReadAllText(f).Trim().ToLowerInvariant() == "night";
            }
            catch (Exception) { Dark = false; }
        }

        public static void Set(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            try
            {
                string f = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, dark ? "night" : "normal");
            }
            catch (Exception) { }
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        // ---------- colors ----------

        public static readonly Color DarkBack = Color.FromArgb(30, 31, 34);
        public static readonly Color DarkCard = Color.FromArgb(44, 45, 49);
        public static readonly Color DarkInput = Color.FromArgb(56, 57, 62);

        // Fill of a normal button in the night theme: a bit lighter than the card it sits on.
        public static Color Btn(Color c)
        {
            Color m = B(c);
            return Dark && m == DarkCard ? Color.FromArgb(62, 64, 70) : m;
        }

        static float Sat(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            return max == 0 ? 0f : (max - min) / (float)max;
        }

        static int Clamp(double v) { return (int)Math.Max(0, Math.Min(255, v)); }

        // A background color of the normal theme -> the night theme.
        public static Color B(Color c)
        {
            if (!Dark || c.IsEmpty || c.A == 0) return c;
            if (Sat(c) >= 0.2f) return c; // orange buttons, colored fills: keep
            int v = c.R;
            if (v < 100) return c;
            if (v >= 245) return DarkCard;
            if (v >= 238) return DarkBack;
            int g = Clamp(30 + (240 - v) * 1.2);
            return Color.FromArgb(g, g, Clamp(g + 3));
        }

        // A text color of the normal theme -> the night theme.
        public static Color F(Color c)
        {
            if (!Dark || c.IsEmpty || c.A == 0) return c;
            if (Sat(c) < 0.25f)
            {
                int v = (c.R + c.G + c.B) / 3;
                if (v >= 200) return c;
                int g = Clamp(250 - v * 0.75);
                return Color.FromArgb(g, g, g);
            }
            if (c.GetBrightness() >= 0.6f) return c;
            return FromHsl(c.GetHue(), c.GetSaturation(), 0.66f);
        }

        static Color FromHsl(float h, float s, float l)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hk = h / 360.0;
            double[] t = { hk + 1.0 / 3, hk, hk - 1.0 / 3 };
            int[] rgb = new int[3];
            for (int i = 0; i < 3; i++)
            {
                double x = t[i];
                if (x < 0) x += 1; if (x > 1) x -= 1;
                double v = x < 1.0 / 6 ? p + (q - p) * 6 * x : x < 0.5 ? q : x < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - x) * 6 : p;
                rgb[i] = Clamp(v * 255);
            }
            return Color.FromArgb(rgb[0], rgb[1], rgb[2]);
        }

        // ---------- applying to forms ----------

        public static void Attach(Form f)
        {
            L.Attach(f);
            object o;
            if (hooked.TryGetValue(f, out o)) return;
            hooked.Add(f, new object());
            EventHandler h = delegate { Apply(f); };
            Changed += h;
            f.FormClosed += delegate { Changed -= h; };
            f.HandleCreated += delegate { TitleBar(f); };
            Apply(f);
        }

        static bool SelfDrawn(Control c)
        {
            return c is RoundButton || c is Tile || c is PillLabel || c is BarProgress || c is RingProgress || c is ThemeToggle || c is SoftScroll;
        }

        static bool IsInput(Control c) { return c is TextBox || c is ComboBox; }

        static void Capture(Control c)
        {
            Orig o;
            if (origs.TryGetValue(c, out o)) return;
            o = new Orig();
            bool box = c is TextBoxBase || c is ListView || c is ComboBox;
            try
            {
                var props = TypeDescriptor.GetProperties(c);
                o.BackSet = box || props["BackColor"].ShouldSerializeValue(c);
                o.ForeSet = box || props["ForeColor"].ShouldSerializeValue(c);
            }
            catch (Exception) { o.BackSet = box; o.ForeSet = box; }
            o.Back = c.BackColor; o.Fore = c.ForeColor;
            origs.Add(c, o);
            c.ControlAdded += delegate (object s, ControlEventArgs e) { Apply(e.Control); };
        }

        public static void Apply(Control c)
        {
            Capture(c);
            Orig o;
            origs.TryGetValue(c, out o);
            Form form = c as Form;
            if (!SelfDrawn(c))
            {
                bool statusFore = c is StatusLabel;
                if (o.BackSet) c.BackColor = Dark ? (IsInput(c) ? DarkInput : B(o.Back)) : o.Back;
                if (!statusFore)
                {
                    if (o.ForeSet) c.ForeColor = Dark ? F(o.Fore) : o.Fore;
                    else if (form != null) { if (Dark) c.ForeColor = F(Color.Black); else c.ResetForeColor(); }
                }
            }
            if (c is ListView || c is RichTextBox || c is TextBox || c is ComboBox) NativeTheme(c);
            if (form != null) TitleBar(form);
            foreach (Control ch in c.Controls) Apply(ch);
            c.Invalidate();
        }

        static void NativeTheme(Control c)
        {
            try
            {
                object marked;
                bool was = nativeDark.TryGetValue(c, out marked);
                if (!Dark && !was) return; // normal theme: leave the control exactly as Windows draws it
                if (!c.IsHandleCreated) { c.HandleCreated += delegate { NativeTheme(c); }; return; }
                if (Dark) { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); if (!was) nativeDark.Add(c, new object()); }
                else { SetWindowTheme(c.Handle, null, null); nativeDark.Remove(c); }
            }
            catch (Exception) { }
        }

        static void TitleBar(Form f)
        {
            try
            {
                if (!f.IsHandleCreated) return;
                int on = Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch (Exception) { }
        }

        // Dark pieces of the wordmark (the text) become light, so it stays readable on the night header. The orange logo is not touched.
        public static Bitmap LightenDarkPixels(Image src)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(d.Stride) * bmp.Height;
                byte[] px = new byte[bytes];
                Marshal.Copy(d.Scan0, px, 0, bytes);
                for (int i = 0; i + 3 < bytes; i += 4)
                {
                    int b = px[i], g2 = px[i + 1], r = px[i + 2];
                    int max = Math.Max(r, Math.Max(g2, b)), min = Math.Min(r, Math.Min(g2, b));
                    if (px[i + 3] == 0 || max - min > 40 || max >= 200) continue;
                    byte v = (byte)Clamp(250 - ((r + g2 + b) / 3) * 0.75);
                    px[i] = v; px[i + 1] = v; px[i + 2] = v;
                }
                Marshal.Copy(px, 0, d.Scan0, bytes);
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }
    }

    // Small soft switch: a pale pill with a round knob. Sun = normal, moon = night.
    class ThemeToggle : Control
    {
        double pos;          // 0 = normal (knob left), 1 = night (knob right)
        bool hover;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 15 };
        readonly ToolTip tip = new ToolTip();

        public ThemeToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(64, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
            pos = Theme.Dark ? 1 : 0;
            anim.Tick += delegate
            {
                double target = Theme.Dark ? 1 : 0;
                pos += (target - pos) * 0.35;
                if (Math.Abs(target - pos) < 0.02) { pos = target; anim.Stop(); }
                Invalidate();
            };
            EventHandler h = delegate { UpdateTip(); anim.Start(); };
            Theme.Changed += h;
            Disposed += delegate { Theme.Changed -= h; anim.Stop(); anim.Dispose(); tip.Dispose(); };
            UpdateTip();
        }

        void UpdateTip() { tip.SetToolTip(this, Theme.Dark ? "Night mode - click for normal" : "Normal mode - click for night"); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Theme.Set(!Theme.Dark); base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); return; }
            base.OnKeyDown(e);
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color trackA = Color.FromArgb(238, 242, 248), borderA = Color.FromArgb(212, 219, 230);
            Color trackB = Color.FromArgb(62, 65, 76), borderB = Color.FromArgb(88, 92, 106);
            Color track = Mix(trackA, trackB, pos), border = Mix(borderA, borderB, pos);
            if (hover) track = Mix(track, pos > 0.5 ? Color.FromArgb(78, 82, 96) : Color.FromArgb(252, 236, 218), 0.5);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Gfx.Round(rect, Height / 2))
            {
                using (var br = new SolidBrush(track)) g.FillPath(br, path);
                using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
            }
            int kd = Height - 8;
            float kx = 4 + (float)(pos * (Width - 8 - kd));
            var knob = new RectangleF(kx, 4, kd, kd);
            // faint icon of the other mode on the empty side
            float ox = pos < 0.5 ? Width - 4 - kd : 4;
            if (pos < 0.5) DrawMoon(g, new RectangleF(ox, 4, kd, kd), Color.FromArgb(150, 160, 185), track);
            else DrawSun(g, new RectangleF(ox, 4, kd, kd), Color.FromArgb(150, 130, 110));
            using (var kb = new SolidBrush(Mix(Color.White, Color.FromArgb(232, 234, 242), pos))) g.FillEllipse(kb, knob);
            using (var kp = new Pen(Mix(Color.FromArgb(222, 226, 234), Color.FromArgb(160, 165, 182), pos), 1f)) g.DrawEllipse(kp, knob);
            if (pos < 0.5) DrawSun(g, knob, Color.FromArgb(232, 128, 30));
            else DrawMoon(g, knob, Color.FromArgb(70, 82, 140), Color.FromArgb(232, 234, 242));
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Color.FromArgb(110, 120, 120, 120)) { DashStyle = DashStyle.Dot })
                    g.DrawPath(fp, Gfx.Round(new Rectangle(2, 2, Width - 5, Height - 5), Height / 2));
        }

        static void DrawSun(Graphics g, RectangleF r, Color c)
        {
            float cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            using (var p = new Pen(c, 1.4f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, (float)(cx + Math.Cos(a) * 6.2), (float)(cy + Math.Sin(a) * 6.2), (float)(cx + Math.Cos(a) * 8.2), (float)(cy + Math.Sin(a) * 8.2));
                }
            }
        }

        static void DrawMoon(Graphics g, RectangleF r, Color moon, Color behind)
        {
            float cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - 6.5f, cy - 6.5f, 13, 13);
                using (var bite = new GraphicsPath())
                {
                    bite.AddEllipse(cx - 1.5f, cy - 8f, 12, 12);
                    using (var reg = new Region(path))
                    {
                        reg.Exclude(bite);
                        using (var b = new SolidBrush(moon)) g.FillRegion(b, reg);
                    }
                }
            }
        }
    }
}
