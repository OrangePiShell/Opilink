using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpiLink
{
    // Small flags for the language box, drawn with GDI+ (Windows cannot show flag emoji in WinForms).
    static class Flags
    {
        public static void Draw(Graphics g, string code, Rectangle r)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(r);
            switch (code)
            {
                case "fa": Bands(g, r, Color.FromArgb(35, 159, 64), Color.White, Color.FromArgb(218, 0, 0)); Dot(g, r, Color.FromArgb(218, 0, 0), 3.2f); break;
                case "ru": Bands(g, r, Color.White, Color.FromArgb(0, 57, 166), Color.FromArgb(213, 43, 30)); break;
                case "hi": Bands(g, r, Color.FromArgb(255, 153, 51), Color.White, Color.FromArgb(19, 136, 8)); Chakra(g, r); break;
                case "zh": China(g, r); break;
                default: Uk(g, r); break;
            }
            g.ResetClip();
            using (Pen p = new Pen(Color.FromArgb(150, 150, 150)))
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            g.Restore(st);
        }

        static void Bands(Graphics g, Rectangle r, Color a, Color b, Color c)
        {
            int y1 = r.Top + r.Height / 3, y2 = r.Top + (2 * r.Height) / 3;
            using (Brush ba = new SolidBrush(a)) g.FillRectangle(ba, r.Left, r.Top, r.Width, y1 - r.Top);
            using (Brush bb = new SolidBrush(b)) g.FillRectangle(bb, r.Left, y1, r.Width, y2 - y1);
            using (Brush bc = new SolidBrush(c)) g.FillRectangle(bc, r.Left, y2, r.Width, r.Bottom - y2);
        }

        static void Dot(Graphics g, Rectangle r, Color c, float d)
        {
            using (Brush b = new SolidBrush(c))
                g.FillEllipse(b, r.Left + r.Width / 2f - d / 2f, r.Top + r.Height / 2f - d / 2f, d, d);
        }

        static void Chakra(Graphics g, Rectangle r)
        {
            using (Pen p = new Pen(Color.FromArgb(0, 0, 128), 1f))
                g.DrawEllipse(p, r.Left + r.Width / 2f - 2.5f, r.Top + r.Height / 2f - 2.5f, 5f, 5f);
        }

        static void China(Graphics g, Rectangle r)
        {
            using (Brush red = new SolidBrush(Color.FromArgb(222, 41, 16))) g.FillRectangle(red, r);
            Color gold = Color.FromArgb(255, 222, 0);
            Star(g, gold, r.Left + r.Width * 0.22f, r.Top + r.Height * 0.30f, 3.8f);
            Star(g, gold, r.Left + r.Width * 0.45f, r.Top + r.Height * 0.14f, 1.2f);
            Star(g, gold, r.Left + r.Width * 0.54f, r.Top + r.Height * 0.28f, 1.2f);
            Star(g, gold, r.Left + r.Width * 0.54f, r.Top + r.Height * 0.46f, 1.2f);
            Star(g, gold, r.Left + r.Width * 0.45f, r.Top + r.Height * 0.60f, 1.2f);
        }

        static void Star(Graphics g, Color c, float cx, float cy, float radius)
        {
            PointF[] pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double ang = (-90 + i * 36) * Math.PI / 180.0;
                float rad = (i % 2 == 0) ? radius : radius * 0.382f;
                pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
            }
            using (Brush b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        static void Uk(Graphics g, Rectangle r)
        {
            Color red = Color.FromArgb(200, 16, 46);
            float h = r.Height;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            using (Brush b = new SolidBrush(Color.FromArgb(1, 33, 105))) g.FillRectangle(b, r);
            using (Pen p = new Pen(Color.White, h * 0.22f))
            {
                g.DrawLine(p, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Top);
            }
            using (Pen p = new Pen(red, h * 0.08f))
            {
                g.DrawLine(p, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Top);
            }
            using (Pen p = new Pen(Color.White, h * 0.36f))
            {
                g.DrawLine(p, cx, r.Top, cx, r.Bottom);
                g.DrawLine(p, r.Left, cy, r.Right, cy);
            }
            using (Pen p = new Pen(red, h * 0.2f))
            {
                g.DrawLine(p, cx, r.Top, cx, r.Bottom);
                g.DrawLine(p, r.Left, cy, r.Right, cy);
            }
        }
    }
}
