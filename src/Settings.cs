using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Copixy
{
    class Settings
    {
        // General
        public bool AutoStart = false;
        public bool StartInTray = true;
        public bool ToolsInMenu = true;          // drawing tools under a chevron
        public int UiSize = 0;                   // toolbar size: 0 Normal, 1 Compact
        public bool ShowHint = true;             // "Select an area" hint in the middle of the screen at capture start
        public bool CheckUpdates = true;         // check GitHub for a newer release once a day
        public long LastUpdateCheck = 0;         // DateTime.UtcNow.Ticks of the last successful check
        public string LastNotifiedVersion = "";  // newest version already announced automatically

        /// <summary>Multiplier applied on top of the monitor DPI scale for the capture UI.</summary>
        public float UiFactor { get { return UiSize == 1 ? 0.85f : 1f; } }
        public Keys HotKey = Keys.PrintScreen;   // key
        public Keys HotMods = Keys.None;         // Control | Shift | Alt (Win is stored separately)
        public bool HotWin = false;

        // Saving
        public string Format = "png";            // png | jpg
        public int JpgQuality = 90;
        public string SaveFolder = DefaultFolder();
        public bool AskSaveDialog = true;
        public string FileBase = "Screenshot";
        public bool AddNumber = true;
        public bool AddDate = true;
        public bool AddTime = false;
        public int Counter = 1;

        // Drawing
        public string FontFamily = "Inter";
        public int Thickness = 4;
        public int TextSize = 28;
        public int TextWeight = 600;
        public int ColorArgb = unchecked((int)0xFFFF3B30);
        public bool LineArrow = false;
        public bool Dashed = false;
        public int EraserSize = 24;
        public int CursorSize = 1;             // 0 S, 1 M, 2 L
        public int Opacity = 100;              // 10..100 %
        public BlendMode Blend = BlendMode.Normal;

        // Magnifier (hold Z)
        public int ZoomFactor = 6;
        public int ZoomSize = 300;

        public Color DrawColor { get { return Color.FromArgb(ColorArgb); } set { ColorArgb = value.ToArgb(); } }

        public static string AppDataDir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string d = Path.Combine(appData, "Copixy");
                Directory.CreateDirectory(d);
                // one-time migration of settings from when the app was called Screenshoter
                try
                {
                    string oldIni = Path.Combine(Path.Combine(appData, "Screenshoter"), "settings.ini");
                    string newIni = Path.Combine(d, "settings.ini");
                    if (!File.Exists(newIni) && File.Exists(oldIni)) File.Copy(oldIni, newIni);
                }
                catch { }
                return d;
            }
        }

        static string FilePath { get { return Path.Combine(AppDataDir, "settings.ini"); } }

        static string DefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        public static bool FirstRun { get; private set; }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) { FirstRun = true; return s; }
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { return s; }

            s.AutoStart = B(d, "AutoStart", s.AutoStart);
            s.StartInTray = B(d, "StartInTray", s.StartInTray);
            s.ToolsInMenu = B(d, "ToolsInMenu", s.ToolsInMenu);
            s.UiSize = Clamp(I(d, "UiSize", s.UiSize), 0, 1);
            s.CheckUpdates = B(d, "CheckUpdates", s.CheckUpdates);
            s.ShowHint = B(d, "ShowHint", s.ShowHint);
            { string v; long t; if (d.TryGetValue("LastUpdateCheck", out v) && long.TryParse(v, out t)) s.LastUpdateCheck = t; }
            { string v; if (d.TryGetValue("LastNotifiedVersion", out v)) s.LastNotifiedVersion = v; }
            s.HotKey = (Keys)I(d, "HotKey", (int)s.HotKey);
            s.HotMods = (Keys)I(d, "HotMods", (int)s.HotMods);
            s.HotWin = B(d, "HotWin", s.HotWin);
            s.Format = Str(d, "Format", s.Format) == "jpg" ? "jpg" : "png";
            s.JpgQuality = Clamp(I(d, "JpgQuality", s.JpgQuality), 1, 100);
            s.SaveFolder = Str(d, "SaveFolder", s.SaveFolder);
            s.AskSaveDialog = B(d, "AskSaveDialog", s.AskSaveDialog);
            s.FileBase = Str(d, "FileBase", s.FileBase);
            s.AddNumber = B(d, "AddNumber", s.AddNumber);
            s.AddDate = B(d, "AddDate", s.AddDate);
            s.AddTime = B(d, "AddTime", s.AddTime);
            s.Counter = Math.Max(1, I(d, "Counter", s.Counter));
            s.FontFamily = Str(d, "FontFamily", s.FontFamily);
            s.Thickness = Clamp(I(d, "Thickness", s.Thickness), 1, 40);
            s.TextSize = Clamp(I(d, "TextSize", s.TextSize), 8, 200);
            s.TextWeight = Clamp(I(d, "TextWeight", s.TextWeight), 300, 700);
            s.ColorArgb = I(d, "Color", s.ColorArgb);
            s.LineArrow = B(d, "LineArrow", s.LineArrow);
            s.Dashed = B(d, "Dashed", s.Dashed);
            s.EraserSize = Clamp(I(d, "EraserSize", s.EraserSize), 4, 200);
            s.Opacity = Clamp(I(d, "Opacity", s.Opacity), 10, 100);
            s.CursorSize = Clamp(I(d, "CursorSize", s.CursorSize), 0, 2);
            try { s.Blend = (BlendMode)Enum.Parse(typeof(BlendMode), Str(d, "Blend", "Normal"), true); } catch { s.Blend = BlendMode.Normal; }
            s.ZoomFactor = Clamp(I(d, "ZoomFactor", s.ZoomFactor), 2, 16);
            s.ZoomSize = Clamp(I(d, "ZoomSize", s.ZoomSize), 100, 800);
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("AutoStart=" + AutoStart);
            sb.AppendLine("StartInTray=" + StartInTray);
            sb.AppendLine("ToolsInMenu=" + ToolsInMenu);
            sb.AppendLine("UiSize=" + UiSize);
            sb.AppendLine("CheckUpdates=" + CheckUpdates);
            sb.AppendLine("ShowHint=" + ShowHint);
            sb.AppendLine("LastUpdateCheck=" + LastUpdateCheck);
            sb.AppendLine("LastNotifiedVersion=" + LastNotifiedVersion);
            sb.AppendLine("HotKey=" + (int)HotKey);
            sb.AppendLine("HotMods=" + (int)HotMods);
            sb.AppendLine("HotWin=" + HotWin);
            sb.AppendLine("Format=" + Format);
            sb.AppendLine("JpgQuality=" + JpgQuality);
            sb.AppendLine("SaveFolder=" + SaveFolder);
            sb.AppendLine("AskSaveDialog=" + AskSaveDialog);
            sb.AppendLine("FileBase=" + FileBase);
            sb.AppendLine("AddNumber=" + AddNumber);
            sb.AppendLine("AddDate=" + AddDate);
            sb.AppendLine("AddTime=" + AddTime);
            sb.AppendLine("Counter=" + Counter);
            sb.AppendLine("FontFamily=" + FontFamily);
            sb.AppendLine("Thickness=" + Thickness);
            sb.AppendLine("TextSize=" + TextSize);
            sb.AppendLine("TextWeight=" + TextWeight);
            sb.AppendLine("Color=" + ColorArgb.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("LineArrow=" + LineArrow);
            sb.AppendLine("Dashed=" + Dashed);
            sb.AppendLine("EraserSize=" + EraserSize);
            sb.AppendLine("Opacity=" + Opacity);
            sb.AppendLine("CursorSize=" + CursorSize);
            sb.AppendLine("Blend=" + Blend);
            sb.AppendLine("ZoomFactor=" + ZoomFactor);
            sb.AppendLine("ZoomSize=" + ZoomSize);
            try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
        }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        // ---------- file name ----------

        public string BuildName(DateTime t, int number)
        {
            var parts = new List<string>();
            string b = SanitizeName(FileBase);
            if (b.Length > 0) parts.Add(b);
            if (AddDate) parts.Add(t.ToString("yyyy-MM-dd"));
            if (AddTime) parts.Add(t.ToString("HH-mm-ss"));
            if (AddNumber) parts.Add(number.ToString("000"));
            if (parts.Count == 0) parts.Add("Screenshot");
            return string.Join("_", parts.ToArray());
        }

        /// <summary>A free file name (without extension) in the save folder; usedNumber is the number that was used.</summary>
        public string NextFreeName(string folder, string ext, out int usedNumber)
        {
            DateTime now = DateTime.Now;
            int n = Counter;
            string name = BuildName(now, n);
            int guard = 0;
            while (File.Exists(Path.Combine(folder, name + "." + ext)) && guard++ < 100000)
            {
                if (AddNumber) { n++; name = BuildName(now, n); }
                else name = BuildName(now, n) + "_" + (guard + 1);
            }
            usedNumber = n;
            return name;
        }

        public static string SanitizeName(string s)
        {
            if (s == null) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            return s.Trim();
        }

        // ---------- hotkey ----------

        public string HotkeyText { get { return HotkeyToText(HotKey, HotMods, HotWin); } }

        public static string HotkeyToText(Keys key, Keys mods, bool win)
        {
            if (key == Keys.None) return "None";
            var sb = new StringBuilder();
            if (win) sb.Append("Win + ");
            if ((mods & Keys.Control) != 0) sb.Append("Ctrl + ");
            if ((mods & Keys.Shift) != 0) sb.Append("Shift + ");
            if ((mods & Keys.Alt) != 0) sb.Append("Alt + ");
            sb.Append(KeyName(key));
            return sb.ToString();
        }

        public static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.PrintScreen: return "PrtSc";
                case Keys.Scroll: return "ScrollLock";
                case Keys.Pause: return "Pause";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Oemtilde: return "~";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }

        // ---------- autostart ----------

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void ApplyAutoStart(bool enable)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enable) k.SetValue("Copixy", "\"" + Application.ExecutablePath + "\" --tray");
                    else if (k.GetValue("Copixy") != null) k.DeleteValue("Copixy", false);
                    // remove the old autostart entry from the previous app name
                    if (k.GetValue("Screenshoter") != null) k.DeleteValue("Screenshoter", false);
                }
            }
            catch { }
        }

        // ---------- helpers ----------
        static bool B(Dictionary<string, string> d, string k, bool def) { string v; bool r; return d.TryGetValue(k, out v) && bool.TryParse(v, out r) ? r : def; }
        static int I(Dictionary<string, string> d, string k, int def) { string v; int r; return d.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def; }
        static string Str(Dictionary<string, string> d, string k, string def) { string v; return d.TryGetValue(k, out v) && v.Length > 0 ? v : def; }
        public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
    }
}
