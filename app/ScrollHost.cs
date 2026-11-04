using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpiLink
{
    // Scrolling area with a slim, soft scroll bar that follows the theme (the Windows one cannot be made dark inside a plain panel).
    // Put the controls into Content. The mouse wheel is forwarded by the tiles (see Tile), Tab scrolls to the focused control.
    class ScrollHost : Panel
    {
        public const int BarW = 10;
        public int ContentWidth = 752;
        public readonly Panel Content = new Panel();
        readonly SoftScroll bar = new SoftScroll();
        int contentH, offset;

        public ScrollHost()
        {
            Controls.Add(Content);
            Controls.Add(bar);
            bar.Visible = false;
            bar.Scrolled += delegate { SetOffset(bar.Value); };
            Content.ControlAdded += delegate (object s, ControlEventArgs e)
            {
                Control c = e.Control;
                c.GotFocus += delegate { EnsureVisible(c); };
            };
            Resize += delegate { LayoutAll(); };
        }

        public void SetContentHeight(int h)
        {
            contentH = h;
            LayoutAll();
        }

        void LayoutAll()
        {
            int max = Math.Max(0, contentH - Height);
            if (offset > max) offset = max;
            Content.SetBounds(0, -offset, ContentWidth, Math.Max(contentH, Height));
            bar.Visible = max > 0;
            bar.SetBounds(ContentWidth + 2, 0, BarW, Height);
            bar.Setup(contentH, Height, offset);
        }

        public void ScrollBy(int delta) { SetOffset(offset + delta); }

        void SetOffset(int v)
        {
            int max = Math.Max(0, contentH - Height);
            v = Math.Max(0, Math.Min(max, v));
            if (v == offset) return;
            offset = v;
            Content.Top = -offset;
            bar.Setup(contentH, Height, offset);
        }

        public void EnsureVisible(Control c)
        {
            if (c.Top < offset) SetOffset(c.Top);
            else if (c.Bottom > offset + Height) SetOffset(c.Bottom - Height);
        }
    }

    // Thin vertical scroll bar: a rounded thumb, no arrows.
    class SoftScroll : Control
    {
        public event EventHandler Scrolled;
        public int Value;
        int total = 1, view = 1, grabDy;
        bool hover, drag;

        public SoftScroll()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = ScrollHost.BarW;
        }

        public void Setup(int totalH, int viewH, int value)
        {
            total = Math.Max(1, totalH); view = Math.Max(1, viewH); Value = value;
            Invalidate();
        }

        Rectangle Thumb()
        {
            if (total <= view || Height <= 0) return Rectangle.Empty;
            int th = Math.Max(30, (int)((long)Height * view / total));
            int range = Math.Max(0, Height - th), max = total - view;
            int y = max > 0 ? (int)((long)range * Value / max) : 0;
            return new Rectangle(1, y, Math.Max(2, Width - 2), th);
        }

        void Raise(int v)
        {
            Value = Math.Max(0, Math.Min(Math.Max(0, total - view), v));
            if (Scrolled != null) Scrolled(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle t = Thumb();
            if (!t.IsEmpty)
            {
                if (t.Contains(e.Location)) { drag = true; grabDy = e.Y - t.Y; }
                else Raise(Value + (e.Y < t.Y ? -view : view));
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag)
            {
                Rectangle t = Thumb();
                int range = Height - t.Height;
                if (range > 0) Raise((int)((long)(e.Y - grabDy) * (total - view) / range));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool strong = hover || drag;
            Color c = Theme.Dark
                ? (strong ? Color.FromArgb(128, 132, 146) : Color.FromArgb(96, 99, 110))
                : (strong ? Color.FromArgb(170, 172, 180) : Color.FromArgb(205, 207, 213));
            using (GraphicsPath p = Gfx.Round(new Rectangle(t.X, t.Y, t.Width - 1, t.Height - 1), Math.Max(2, t.Width / 2 - 1)))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }
}
