using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpiLink
{
    // One tool tile: title + one line. Main = orange. A tile that does not work on this board is grey and shows why (clicks are ignored).
    class Tile : Control
    {
        public string Title = "", Sub = "";
        public bool Main;
        bool available = true;
        string note = "";
        bool hover, down;

        public Tile()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(180, 56);
            Font = new Font("Segoe UI", 8F);
        }

        public bool Available { get { return available; } }

        public void SetAvailable(bool ok, string why)
        {
            available = ok; note = why ?? "";
            Cursor = ok ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        public void SetTitle(string t) { Title = t; Invalidate(); }
        public void SetSub(string t) { Sub = t; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true; Invalidate();
            // The mouse wheel goes to the focused control, so focus the tile under the mouse (keeps the list scrollable).
            Form f = FindForm();
            if (f != null && f.ContainsFocus && !Focused && !(f.ActiveControl is TextBox)) Focus();
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            if (!available) return;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); return; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollHost h = Parent != null ? Parent.Parent as ScrollHost : null;
            if (h != null) h.ScrollBy(-e.Delta);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back = Color.White, border = Color.FromArgb(190, 190, 190), fore = Color.FromArgb(30, 30, 30), sub = Color.FromArgb(110, 110, 110);
            int bw = 1;
            bool orangeHover = false;
            if (!available)
            {
                back = Color.FromArgb(235, 235, 235); border = Color.FromArgb(215, 215, 215); fore = Color.FromArgb(140, 140, 140); sub = Color.FromArgb(150, 150, 150);
            }
            else if (Main)
            {
                back = Color.FromArgb(217, 96, 10); border = Color.FromArgb(180, 78, 6); fore = Color.White; sub = Color.FromArgb(255, 232, 210);
                if (hover || down) back = ControlPaint.Dark(back, down ? 0.1f : 0.05f);
            }
            else if (hover || down)
            {
                back = down ? ControlPaint.Dark(RoundButton.HoverFill, 0.08f) : RoundButton.HoverFill;
                border = RoundButton.HoverBorder; bw = 2; sub = Color.FromArgb(70, 50, 30);
                orangeHover = true;
                if (Theme.Dark) fore = Color.FromArgb(30, 30, 30);
            }
            if (Theme.Dark && !Main && !orangeHover)
            {
                back = available ? Theme.Btn(back) : Color.FromArgb(40, 41, 45);
                border = available ? Theme.B(border) : Color.FromArgb(58, 60, 66);
                fore = available ? Color.FromArgb(236, 236, 240) : Color.FromArgb(120, 122, 130);
                sub = available ? Color.FromArgb(160, 163, 172) : Color.FromArgb(105, 108, 116);
            }
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Gfx.Round(rect, 8))
            {
                using (var br = new SolidBrush(back)) g.FillPath(br, path);
                using (var pen = new Pen(border, bw)) g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
                using (var fp = new Pen(Color.FromArgb(110, 70, 70, 70)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(fp, 4, 4, Width - 9, Height - 9);
            using (var bold = new Font("Segoe UI", 9.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, L.T(Title), bold, new Rectangle(10, 5, Width - 20, 20), fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, L.T(available ? Sub : note), Font, new Rectangle(10, 26, Width - 20, 28), sub,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
