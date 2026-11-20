using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Copixy
{
    static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_SYSCHAR = 0x0106;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_KEYMENU = 0xF100;
        public const int VK_MENU = 0x12;

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        static extern int SetProcessDpiAwareness(int value);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);

        /// <summary>Captures a screen area (SRCCOPY | CAPTUREBLT, so layered/translucent windows are included).</summary>
        public static void CopyScreen(Graphics g, Rectangle src)
        {
            bool ok = false;
            IntPtr hdcDest = g.GetHdc();
            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            try { ok = BitBlt(hdcDest, 0, 0, src.Width, src.Height, hdcSrc, src.Left, src.Top, 0x00CC0020 | 0x40000000); }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcSrc);
                g.ReleaseHdc(hdcDest);
            }
            if (!ok) g.CopyFromScreen(src.Left, src.Top, 0, 0, src.Size, CopyPixelOperation.SourceCopy);
        }

        public static void InitDpi()
        {
            // Per-Monitor v2 -> Per-Monitor v1 -> System aware
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        /// <summary>Scale factor (1.0 = 96 dpi) of the monitor containing the point (screen coordinates).</summary>
        public static float ScaleAt(Point screenPt)
        {
            try
            {
                var p = new POINT { X = screenPt.X, Y = screenPt.Y };
                IntPtr mon = MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }
    }
}
