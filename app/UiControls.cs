using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpiLink
{
    // Thin flat progress bar (orange). Marquee = moving block when the size is unknown.
    class BarProgress : Control
    {
        double pos;
        bool marquee;
        int offset;
        readonly Timer timer = new Timer();
        Color fill = Color.FromArgb(239, 172, 101);

        public BarProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 10;
            timer.Interval = 30;
            timer.Tick += delegate { offset = (offset + 6) % Math.Max(1, Width + 120); Invalidate(); };
        }

        public double Value
        {
            get { return pos; }
            set { pos = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public bool Marquee
        {
            get { return marquee; }
            set { marquee = value; timer.Enabled = value; Invalidate(); }
        }

        public Color Fill
        {
            get { return fill; }
            set { fill = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath tp = Gfx.Round(track, Math.Max(1, (Height - 1) / 2)))
            {
                using (var tb = new SolidBrush(Theme.Dark ? Color.FromArgb(62, 64, 70) : Color.FromArgb(232, 232, 232))) g.FillPath(tb, tp);
                g.SetClip(tp);
                using (var b = new SolidBrush(fill))
                {
                    if (marquee) g.FillRectangle(b, offset - 120, 0, 120, Height);
                    else g.FillRectangle(b, 0, 0, (int)(Width * pos / 100.0), Height);
                }
                g.ResetClip();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // Round progress ring with a percent in the middle (the burn: write 0-50%, verify 50-100%).
    class RingProgress : Control
    {
        int percent;
        string caption = "";

        public RingProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(130, 130);
        }

        public void SetValue(int pct, string text)
        {
            percent = Math.Max(0, Math.Min(100, pct));
            caption = text ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = 14;
            var r = new Rectangle(w / 2 + 1, w / 2 + 1, Math.Min(Width, Height) - w - 3, Math.Min(Width, Height) - w - 3);
            using (var bg = new Pen(Color.FromArgb(230, 230, 230), w)) g.DrawEllipse(bg, r);
            if (percent > 0)
                using (var fg = new Pen(Color.FromArgb(239, 172, 101), w))
                {
                    fg.StartCap = LineCap.Round; fg.EndCap = LineCap.Round;
                    g.DrawArc(fg, r, -90, 360f * percent / 100f);
                }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var big = new Font("Segoe UI", 18F, FontStyle.Bold))
            using (var small = new Font("Segoe UI", 8.5F))
            using (var b1 = new SolidBrush(Color.FromArgb(194, 84, 10)))
            using (var b2 = new SolidBrush(Color.FromArgb(107, 107, 107)))
            {
                g.DrawString(percent + "%", big, b1, new RectangleF(0, Height / 2f - 22, Width, 28), sf);
                g.DrawString(caption, small, b2, new RectangleF(0, Height / 2f + 6, Width, 18), sf);
            }
            sf.Dispose();
        }
    }

    // Rounded-rectangle helper used by the modern controls.
    static class Gfx
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Button with rounded corners. It still is a normal Button, so the existing code (Click, Enabled, BackColor, ForeColor, FlatAppearance) keeps working.
    class RoundButton : Button
    {
        bool hover, down;
        // Hover = the same orange as the selected "Board list" button. Off for buttons with their own color (Next, Download now, Cancel).
        public bool OrangeHover = true;
        public static readonly Color HoverFill = Color.FromArgb(255, 160, 55);
        public static readonly Color HoverBorder = Color.FromArgb(217, 96, 10);

        public RoundButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Theme.Btn(BackColor);
            Color fore = Theme.F(ForeColor);
            Color border = Theme.B(FlatAppearance.BorderColor);
            int bw = FlatAppearance.BorderSize;
            if (!Enabled)
            {
                back = Theme.Dark ? Color.FromArgb(46, 47, 51) : Color.FromArgb(235, 235, 235);
                fore = Theme.Dark ? Color.FromArgb(112, 114, 122) : Color.FromArgb(140, 140, 140);
                border = Theme.Dark ? Color.FromArgb(62, 64, 70) : Color.FromArgb(215, 215, 215);
                if (bw > 0) bw = 1;
            }
            else if (OrangeHover && (hover || down))
            {
                back = down ? ControlPaint.Dark(HoverFill, 0.08f) : HoverFill;
                border = HoverBorder;
                if (Theme.Dark) fore = Color.FromArgb(30, 30, 30);
                bw = 2;
            }
            else if (down) back = ControlPaint.Dark(back, 0.1f);
            else if (hover) back = ControlPaint.Dark(back, 0.04f);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Gfx.Round(rect, 8))
            {
                using (var br = new SolidBrush(back)) g.FillPath(br, path);
                if (bw > 0) using (var pen = new Pen(border, bw)) g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Color.FromArgb(110, 70, 70, 70)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(fp, 4, 4, Width - 9, Height - 9);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // White card with rounded corners and a thin border. Children are normal controls placed inside it.
    class CardPanel : Panel
    {
        public Color Border = Color.FromArgb(218, 218, 218);
        public int Radius = 10;
        public bool Soft; // very light blue -> teal -> purple -> orange gradient (colors from the Orange Pi wallpaper)

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Gfx.Round(rect, Radius))
            {
                if (Soft && rect.Width > 4 && rect.Height > 4)
                {
                    using (var lg = new LinearGradientBrush(rect, Color.White, Color.White, 20f))
                    {
                        var cb = new ColorBlend(4);
                        cb.Colors = Theme.Dark
                            ? new Color[] { Color.FromArgb(38, 44, 54), Color.FromArgb(36, 48, 50), Color.FromArgb(46, 42, 56), Color.FromArgb(56, 46, 40) }
                            : new Color[] { Color.FromArgb(244, 249, 254), Color.FromArgb(246, 252, 252), Color.FromArgb(251, 249, 253), Color.FromArgb(254, 248, 241) };
                        cb.Positions = new float[] { 0f, 0.45f, 0.75f, 1f };
                        lg.InterpolationColors = cb;
                        g.FillPath(lg, path);
                    }
                }
                else
                {
                    using (var br = new SolidBrush(BackColor)) g.FillPath(br, path);
                }
                using (var pen = new Pen(Theme.B(Border), 1f)) g.DrawPath(pen, path);
            }
        }
    }

    // Step label drawn as a rounded pill (active = orange, done = light green, later = grey).
    class PillLabel : Label
    {
        public Color PillColor = Color.FromArgb(225, 225, 225);

        public PillLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Gfx.Round(rect, Math.Max(1, (Height - 1) / 2)))
            using (var br = new SolidBrush(PillColor))
                g.FillPath(br, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
