using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Copixy
{
    /// <summary>Embedded resources: icons, cursor images and the Inter font (SIL OFL).</summary>
    static class Res
    {
        static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        static readonly Dictionary<string, Bitmap> scaled = new Dictionary<string, Bitmap>();

        public static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        public static Bitmap Icon(string name)
        {
            Bitmap b;
            if (icons.TryGetValue(name, out b)) return b;
            using (var s = Open("icon." + name + ".png"))
            {
                if (s == null) return null;
                using (var tmp = new Bitmap(s)) b = new Bitmap(tmp);
            }
            icons[name] = b;
            return b;
        }

        // Icon sets: name_xSm.png … name_2xLg.png (16, 20, 24, 32, 40, 48 px)
        static readonly string[] SizeSuffix = { "xSm", "Sm", "Md", "Lg", "xLg", "2xLg" };
        static readonly int[] SizePx = { 16, 20, 24, 32, 40, 48 };

        // name used in code -> icon set name in resources\icons
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        {
            { "move", "move-default" }, { "line", "line-default" }, { "dashed", "line-dashed-default" },
            { "pencil", "pencil-default" }, { "square", "square-default" }, { "circle", "circle-default" },
            { "type", "text-default" }, { "eraser", "eraser-default" },
            { "undo-2", "undo-default" }, { "redo-2", "redo-default" },
            { "copy", "copy-default" }, { "save", "save-default" }, { "settings", "settings-default" },
            { "x", "close-default" }, { "minus", "minus-default" }, { "plus", "plus-default" },
            { "move-up-right", "move-up-right-default" },
            { "zoom", "zoom-default" },
        };

        static HashSet<string> resNames;
        static bool HasRes(string n)
        {
            if (resNames == null) resNames = new HashSet<string>(Assembly.GetExecutingAssembly().GetManifestResourceNames());
            return resNames.Contains(n);
        }

        static Bitmap LoadRes(string resName)
        {
            Bitmap b;
            if (icons.TryGetValue(resName, out b)) return b;
            using (var s = Open(resName))
            {
                if (s == null) return null;
                using (var tmp = new Bitmap(s)) b = new Bitmap(tmp);
            }
            icons[resName] = b;
            return b;
        }

        /// <summary>
        /// Returns an icon of the requested size. A pre-rendered size within ±15 % is used as is, without scaling;
        /// otherwise the nearest larger size is scaled down. Results are cached.
        /// </summary>
        public static Bitmap IconSized(string name, int size)
        {
            string key = name + "@" + size;
            Bitmap b;
            if (scaled.TryGetValue(key, out b)) return b;

            string set;
            if (!Alias.TryGetValue(name, out set)) set = name;

            Bitmap src = null;
            int best = -1;
            for (int i = 0; i < SizePx.Length; i++)
            {
                if (!HasRes("icon." + set + "_" + SizeSuffix[i] + ".png")) continue;
                if (best < 0) { best = i; continue; }
                int d = Math.Abs(SizePx[i] - size), db = Math.Abs(SizePx[best] - size);
                if (d < db || (d == db && SizePx[i] > SizePx[best])) best = i;
            }
            if (best >= 0)
            {
                if (Math.Abs(SizePx[best] - size) <= Math.Max(1, size * 15 / 100))
                {
                    b = LoadRes("icon." + set + "_" + SizeSuffix[best] + ".png");
                    if (b != null) { scaled[key] = b; return b; }
                }
                // nearest larger (or the largest available) size, then scale it
                int pick = -1, largest = -1;
                for (int i = 0; i < SizePx.Length; i++)
                {
                    if (!HasRes("icon." + set + "_" + SizeSuffix[i] + ".png")) continue;
                    largest = i;                                   // SizePx is sorted ascending
                    if (pick < 0 && SizePx[i] >= size) pick = i;   // the smallest size that is not smaller than requested
                }
                if (pick < 0) pick = largest;
                src = LoadRes("icon." + set + "_" + SizeSuffix[pick] + ".png");
            }
            if (src == null) src = Icon(set);
            if (src == null) return null;

            b = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size));
            }
            scaled[key] = b;
            return b;
        }

        public static Icon AppIcon(int size)
        {
            using (var s = Open("app.ico")) return new Icon(s, size, size);
        }

        // ------------------- fonts -------------------

        static PrivateFontCollection pfc;
        static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern int AddFontResourceEx(string name, uint fl, IntPtr res);

        static readonly string[] Weights = { "Light", "Regular", "Medium", "SemiBold", "Bold" };

        public static void InitFonts()
        {
            pfc = new PrivateFontCollection();
            string dir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Copixy"), "fonts");
            try { Directory.CreateDirectory(dir); } catch { }
            foreach (var w in Weights)
            {
                try
                {
                    string path = Path.Combine(dir, "Inter-" + w + ".ttf");
                    using (var s = Open("font.Inter-" + w + ".ttf"))
                    {
                        if (s == null) continue;
                        if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
                            using (var f = File.Create(path)) s.CopyTo(f);
                    }
                    pfc.AddFontFile(path);
                    try { AddFontResourceEx(path, 0x10 /*FR_PRIVATE*/, IntPtr.Zero); } catch { }
                }
                catch { }
            }
            foreach (var f in pfc.Families) families[f.Name] = f;
            try
            {
                using (var inst = new InstalledFontCollection())
                    foreach (var f in inst.Families)
                        if (!families.ContainsKey(f.Name)) families[f.Name] = f;
            }
            catch { }
        }

        public static List<string> FamilyNames()
        {
            var l = new List<string>();
            foreach (var k in families.Keys) l.Add(k);
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        static FontFamily Find(string name)
        {
            FontFamily f;
            return name != null && families.TryGetValue(name, out f) ? f : null;
        }

        public static string WeightName(int w)
        {
            switch (w)
            {
                case 300: return "Light";
                case 500: return "Medium";
                case 600: return "SemiBold";
                case 700: return "Bold";
                default: return "Regular";
            }
        }

        /// <summary>Font of the given family, size (in pixels) and weight (300..700).</summary>
        public static Font GetFont(string family, float sizePx, int weight)
        {
            string key = family + "|" + sizePx.ToString("0.##") + "|" + weight;
            Font cached;
            if (fontCache.TryGetValue(key, out cached)) return cached;

            Font result = null;
            string wn = WeightName(weight);
            // 1) A separate family per weight: "Inter Medium", "Segoe UI Semibold" ...
            if (weight != 400 && weight != 700)
            {
                FontFamily ff = Find(family + " " + wn);
                if (ff == null && wn == "SemiBold") ff = Find(family + " Semibold");
                if (ff == null && wn == "SemiBold") ff = Find(family + " Semibold");
                if (ff != null && ff.IsStyleAvailable(FontStyle.Regular))
                    result = new Font(ff, sizePx, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            if (result == null)
            {
                FontFamily ff = Find(family) ?? Find("Inter") ?? FontFamily.GenericSansSerif;
                FontStyle st = weight >= 600 ? FontStyle.Bold : FontStyle.Regular;
                if (!ff.IsStyleAvailable(st)) st = FontStyle.Regular;
                if (!ff.IsStyleAvailable(st)) { ff = Find("Inter") ?? FontFamily.GenericSansSerif; st = FontStyle.Regular; }
                result = new Font(ff, sizePx, st, GraphicsUnit.Pixel);
            }
            if (fontCache.Count > 200) fontCache.Clear();
            fontCache[key] = result;
            return result;
        }
    }
}
