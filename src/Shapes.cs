using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Copixy
{
    enum Tool { None, Line, Pencil, Rect, Ellipse, Text, Cursor, Eraser }

    abstract class Shape
    {
        public Color Color;
        public float Width;
        public BlendMode Mode = BlendMode.Normal;
        public float Opacity = 1f;
        public abstract void Draw(Graphics g);
        /// <summary>Area the shape may touch (world coordinates).</summary>
        public abstract RectangleF Extent(Graphics g);

        protected static RectangleF Grow(RectangleF r, float d) { r.Inflate(d, d); return r; }

        protected Pen MakePen()
        {
            var p = new Pen(Color, Width);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }
    }

    class LineShape : Shape
    {
        public PointF A, B;
        public bool Dashed, Arrow;

        public bool IsEmpty { get { return Math.Abs(A.X - B.X) < 1 && Math.Abs(A.Y - B.Y) < 1; } }

        public override RectangleF Extent(Graphics g)
        {
            var r = RectangleF.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y));
            return Grow(r, Math.Max(12f, Width * 3.6f) + Width + 4);
        }

        public override void Draw(Graphics g)
        {
            if (IsEmpty) return;
            using (var p = MakePen())
            {
                if (Dashed)
                {
                    p.DashCap = DashCap.Round;
                    p.DashPattern = new float[] { 2f, 2.2f };
                }
                if (!Arrow) { g.DrawLine(p, A, B); return; }

                float dx = B.X - A.X, dy = B.Y - A.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                float ux = dx / len, uy = dy / len;
                float head = Math.Max(12f, Width * 3.6f);
                if (head > len * 0.8f) head = len * 0.8f;
                float half = head * 0.55f;
                var basePt = new PointF(B.X - ux * head, B.Y - uy * head);
                // end the line slightly inside the arrowhead so the tip stays sharp
                var lineEnd = new PointF(B.X - ux * head * 0.6f, B.Y - uy * head * 0.6f);
                g.DrawLine(p, A, lineEnd);
                var tri = new[]
                {
                    B,
                    new PointF(basePt.X - uy * half, basePt.Y + ux * half),
                    new PointF(basePt.X + uy * half, basePt.Y - ux * half)
                };
                using (var path = new GraphicsPath())
                using (var br = new SolidBrush(Color))
                using (var edge = new Pen(Color, Math.Max(1f, Width * 0.5f)) { LineJoin = LineJoin.Round })
                {
                    path.AddPolygon(tri);
                    g.FillPath(br, path);
                    g.DrawPath(edge, path);
                }
            }
        }
    }

    class PencilShape : Shape
    {
        public readonly List<PointF> Points = new List<PointF>();

        public override RectangleF Extent(Graphics g)
        {
            if (Points.Count == 0) return RectangleF.Empty;
            float l = Points[0].X, t = Points[0].Y, r = l, b = t;
            foreach (var p in Points) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y); }
            return Grow(RectangleF.FromLTRB(l, t, r, b), Width / 2 + 3);
        }

        public override void Draw(Graphics g)
        {
            if (Points.Count == 0) return;
            if (Points.Count == 1)
            {
                using (var b = new SolidBrush(Color))
                    g.FillEllipse(b, Points[0].X - Width / 2, Points[0].Y - Width / 2, Width, Width);
                return;
            }
            using (var p = MakePen())
            using (var path = new GraphicsPath())
            {
                path.AddLines(Points.ToArray());
                g.DrawPath(p, path);
            }
        }
    }

    /// <summary>Eraser: paints with a brush made of the original screenshot, erasing everything drawn before.</summary>
    class EraserShape : PencilShape
    {
        public static Brush Source;   // TextureBrush of the original screenshot (set by the capture window)

        public override void Draw(Graphics g)
        {
            if (Source == null || Points.Count == 0) return;
            if (Points.Count == 1)
            {
                g.FillEllipse(Source, Points[0].X - Width / 2, Points[0].Y - Width / 2, Width, Width);
                return;
            }
            using (var p = new Pen(Source, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var path = new GraphicsPath())
            {
                path.AddLines(Points.ToArray());
                g.DrawPath(p, path);
            }
        }
    }

    /// <summary>"Erase all": covers everything drawn before with the original screenshot (undoable).</summary>
    class EraseAllShape : Shape
    {
        public override RectangleF Extent(Graphics g) { return new RectangleF(-100000, -100000, 200000, 200000); }

        public override void Draw(Graphics g)
        {
            if (EraserShape.Source == null) return;
            g.FillRectangle(EraserShape.Source, g.VisibleClipBounds);
        }
    }

    /// <summary>Cursor image placed on the screenshot. Stays an object: it can be moved and deleted.</summary>
    class StampShape : Shape
    {
        public static readonly string[] Names = { "Cursor-Sm", "Cursor-Md", "Cursor-Lg" };
        public Point Pos;          // top-left corner
        public int SizeIdx;

        public Bitmap Image { get { return Res.Icon(Names[Math.Max(0, Math.Min(2, SizeIdx))]); } }

        public Rectangle Rect
        {
            get { var b = Image; return b == null ? Rectangle.Empty : new Rectangle(Pos.X, Pos.Y, b.Width, b.Height); }
        }

        /// <summary>Arrow tip position relative to the image's top-left corner.</summary>
        public Point HotSpot { get { var b = Image; return b == null ? Point.Empty : new Point(b.Width / 8, b.Height / 8); } }

        public override RectangleF Extent(Graphics g) { return Rect; }

        public override void Draw(Graphics g)
        {
            var b = Image;
            if (b == null) return;
            var im = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(b, Rect, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = im; g.PixelOffsetMode = po;
        }
    }

    class BoxShape : Shape
    {
        public RectangleF R;
        public bool Ellipse;
        public bool Dashed;

        public bool IsEmpty { get { return R.Width < 1 && R.Height < 1; } }

        public override RectangleF Extent(Graphics g) { return Grow(R, Width / 2 + 3); }

        public override void Draw(Graphics g)
        {
            if (IsEmpty) return;
            using (var p = MakePen())
            {
                p.LineJoin = LineJoin.Miter;
                if (Dashed)
                {
                    p.DashCap = DashCap.Round;
                    p.DashPattern = new float[] { 2f, 2.2f };
                }
                if (Ellipse) g.DrawEllipse(p, R);
                else g.DrawRectangle(p, R.X, R.Y, R.Width, R.Height);
            }
        }
    }

    class TextShape : Shape
    {
        public PointF Pos;
        public string Text = "";
        public string Family;
        public float Size;
        public int Weight;

        static StringFormat fmt;
        public static StringFormat Format
        {
            get
            {
                if (fmt == null)
                {
                    fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
                    fmt.FormatFlags = (fmt.FormatFlags | StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap) & ~StringFormatFlags.LineLimit;
                }
                return fmt;
            }
        }

        public Font Font { get { return Res.GetFont(Family, Size, Weight); } }

        public override RectangleF Extent(Graphics g) { return Grow(Bounds(g), Size * 0.3f + 4); }

        public float LineHeight(Graphics g)
        {
            try { return Font.GetHeight(g); }
            catch { return Size * 1.21f; }
        }

        public override void Draw(Graphics g)
        {
            if (string.IsNullOrEmpty(Text)) return;
            using (var b = new SolidBrush(Color))
            {
                float lh = LineHeight(g);
                string[] lines = Text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Length > 0)
                        FillText(g, lines[i], Font, b, new PointF(Pos.X, Pos.Y + i * lh), Format);
            }
        }

        /// <summary>
        /// Text drawn as a vector outline: anti-aliasing is always on and does not depend
        /// on ClearType settings or the font's hinting tables.
        /// </summary>
        public static void FillText(Graphics g, string text, Font font, Brush brush, PointF origin, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(text)) return;
            var oldSm = g.SmoothingMode;
            var oldPo = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, origin, fmt);
                g.FillPath(brush, path);
            }
            g.SmoothingMode = oldSm;
            g.PixelOffsetMode = oldPo;
        }

        public static void FillText(Graphics g, string text, Font font, Brush brush, RectangleF rect, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(text)) return;
            var oldSm = g.SmoothingMode;
            var oldPo = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, rect, fmt);
                g.FillPath(brush, path);
            }
            g.SmoothingMode = oldSm;
            g.PixelOffsetMode = oldPo;
        }

        /// <summary>Position and height of the caret at the end of the text.</summary>
        public RectangleF Caret(Graphics g)
        {
            float lh = LineHeight(g);
            string[] lines = Text.Split('\n');
            string last = lines[lines.Length - 1];
            float w = last.Length == 0 ? 0 : g.MeasureString(last, Font, PointF.Empty, Format).Width;
            return new RectangleF(Pos.X + w + 1, Pos.Y + (lines.Length - 1) * lh, Math.Max(1.5f, Size / 14f), lh);
        }

        public RectangleF Bounds(Graphics g)
        {
            float lh = LineHeight(g);
            string[] lines = Text.Split('\n');
            float w = 0;
            foreach (var l in lines)
                if (l.Length > 0) w = Math.Max(w, g.MeasureString(l, Font, PointF.Empty, Format).Width);
            return new RectangleF(Pos.X, Pos.Y, Math.Max(w, Size * 0.6f), lines.Length * lh);
        }
    }
}
