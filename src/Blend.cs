using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Copixy
{
    enum BlendMode { Normal, Multiply, Screen, Overlay, SoftLight, Darken, Lighten, Difference }

    /// <summary>
    /// Composites shapes with opacity and blend modes.
    /// GDI+ only supports normal (source-over) blending, so a shape is first drawn into a separate layer
    /// and then blended per pixel with the image underneath.
    /// </summary>
    static class Compositor
    {
        static readonly byte[][] luts = new byte[8][];
        static Bitmap measureBmp;
        static Graphics measureG;

        public static string Name(BlendMode m)
        {
            return m == BlendMode.SoftLight ? "Soft light" : m.ToString();
        }

        public static bool NeedsLayer(Shape s)
        {
            if (s is EraserShape || s is EraseAllShape) return false;
            return s.Mode != BlendMode.Normal || s.Opacity < 0.999f;
        }

        public static Graphics Measure
        {
            get
            {
                if (measureG == null)
                {
                    measureBmp = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
                    measureG = Graphics.FromImage(measureBmp);
                }
                return measureG;
            }
        }

        public static Rectangle Bounds(Shape s)
        {
            RectangleF f = s.Extent(Measure);
            return Rectangle.FromLTRB((int)Math.Floor(f.Left), (int)Math.Floor(f.Top), (int)Math.Ceiling(f.Right), (int)Math.Ceiling(f.Bottom));
        }

        /// <summary>Applies a shape to target. origin is the world coordinate of target's (0,0).</summary>
        public static void Apply(Bitmap target, Point origin, Shape s, Bitmap shot, bool asObject = false)
        {
            // cursors and text are objects above the drawings; they are not baked into the canvas
            if (!asObject && (s is StampShape || s is TextShape)) return;
            var targetWorld = new Rectangle(origin, target.Size);

            if (s is EraseAllShape)
            {
                using (var g = Graphics.FromImage(target))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(shot, new Rectangle(0, 0, target.Width, target.Height), targetWorld, GraphicsUnit.Pixel);
                }
                return;
            }

            if (!NeedsLayer(s))
            {
                using (var g = Graphics.FromImage(target))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(-origin.X, -origin.Y);
                    s.Draw(g);
                }
                return;
            }

            Rectangle b = Rectangle.Intersect(Bounds(s), targetWorld);
            if (b.Width <= 0 || b.Height <= 0) return;
            using (var layer = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb))
            {
                using (var lg = Graphics.FromImage(layer))
                {
                    lg.SmoothingMode = SmoothingMode.AntiAlias;
                    lg.TranslateTransform(-b.X, -b.Y);
                    s.Draw(lg);
                }
                Composite(target, new Rectangle(b.X - origin.X, b.Y - origin.Y, b.Width, b.Height), layer, s.Mode, s.Opacity);
            }
        }

        static void Composite(Bitmap target, Rectangle tr, Bitmap layer, BlendMode mode, float opacity)
        {
            BitmapData td = target.LockBits(tr, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            BitmapData ld = layer.LockBits(new Rectangle(0, 0, tr.Width, tr.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int w = tr.Width;
                var trow = new int[w];
                var lrow = new int[w];
                byte[] lut = mode == BlendMode.Normal ? null : Lut(mode);
                int op = (int)Math.Round(Math.Max(0f, Math.Min(1f, opacity)) * 256);

                for (int y = 0; y < tr.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(ld.Scan0, y * ld.Stride), lrow, 0, w);
                    bool any = false;
                    for (int x = 0; x < w; x++) if ((lrow[x] & unchecked((int)0xFF000000)) != 0) { any = true; break; }
                    if (!any) continue;

                    IntPtr tp = IntPtr.Add(td.Scan0, y * td.Stride);
                    Marshal.Copy(tp, trow, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        int l = lrow[x];
                        int a = (l >> 24) & 255;
                        if (a == 0) continue;
                        int sr = (l >> 16) & 255, sg = (l >> 8) & 255, sb = l & 255;
                        if (a < 255)
                        {
                            sr = Math.Min(255, sr * 255 / a);
                            sg = Math.Min(255, sg * 255 / a);
                            sb = Math.Min(255, sb * 255 / a);
                        }
                        int t = trow[x];
                        int ta = (t >> 24) & 255;
                        int dr = (t >> 16) & 255, dg = (t >> 8) & 255, db = t & 255;

                        int br = lut == null ? sr : lut[(dr << 8) | sr];
                        int bg = lut == null ? sg : lut[(dg << 8) | sg];
                        int bb = lut == null ? sb : lut[(db << 8) | sb];

                        int k = (a * op) >> 8; // 0..255
                        dr += (br - dr) * k / 255;
                        dg += (bg - dg) * k / 255;
                        db += (bb - db) * k / 255;
                        trow[x] = (ta << 24) | (dr << 16) | (dg << 8) | db;
                    }
                    Marshal.Copy(trow, 0, tp, w);
                }
            }
            finally
            {
                layer.UnlockBits(ld);
                target.UnlockBits(td);
            }
        }

        /// <summary>Lookup table B(cb, cs) for a blend mode, indexed by (cb &lt;&lt; 8) | cs.</summary>
        static byte[] Lut(BlendMode m)
        {
            int i = (int)m;
            if (luts[i] != null) return luts[i];
            var t = new byte[65536];
            for (int cb = 0; cb < 256; cb++)
                for (int cs = 0; cs < 256; cs++)
                {
                    double b = cb / 255.0, s = cs / 255.0, r;
                    switch (m)
                    {
                        case BlendMode.Multiply: r = b * s; break;
                        case BlendMode.Screen: r = b + s - b * s; break;
                        case BlendMode.Overlay: r = b <= 0.5 ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s); break;
                        case BlendMode.SoftLight:
                            if (s <= 0.5) r = b - (1 - 2 * s) * b * (1 - b);
                            else
                            {
                                double d = b <= 0.25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b);
                                r = b + (2 * s - 1) * (d - b);
                            }
                            break;
                        case BlendMode.Darken: r = Math.Min(b, s); break;
                        case BlendMode.Lighten: r = Math.Max(b, s); break;
                        case BlendMode.Difference: r = Math.Abs(b - s); break;
                        default: r = s; break;
                    }
                    t[(cb << 8) | cs] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(r * 255)));
                }
            luts[i] = t;
            return t;
        }
    }
}
