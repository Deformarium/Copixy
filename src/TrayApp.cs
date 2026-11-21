using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Copixy
{
    /// <summary>Background app: tray icon, global hotkey, starting a capture.</summary>
    class TrayApp : ApplicationContext
    {
        readonly Settings S;
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        readonly SynchronizationContext ui;
        readonly ToolStripMenuItem captureItem;
        CaptureForm capture;
        SettingsForm settingsForm;
        string lastSavedPath;
        string balloonUrl;               // if set, clicking the balloon opens this page
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        bool checkingUpdates;
        bool hotkeyOk;

        public TrayApp(EventWaitHandle wakeEvent, bool fromAutostart)
        {
            S = Settings.Load();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += () => BeginCapture(0);

            var menu = new ContextMenuStrip();
            captureItem = new ToolStripMenuItem("Take screenshot", null, delegate { BeginCapture(200); });
            captureItem.Font = new Font(captureItem.Font, FontStyle.Bold);
            menu.Items.Add(captureItem);
            menu.Items.Add(new ToolStripMenuItem("Open screenshots folder", null, delegate { OpenFolder(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Check for updates…", null, delegate { CheckForUpdates(true); }));
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, delegate { ShowSettings(null); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitThread(); }));

            tray = new NotifyIcon
            {
                Icon = Res.AppIcon(SystemInformation.SmallIconSize.Width),
                Text = "Copixy",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) BeginCapture(200); };
            tray.BalloonTipClicked += delegate
            {
                if (balloonUrl != null)
                {
                    try { Process.Start(balloonUrl); } catch { }
                    return;
                }
                if (lastSavedPath != null && File.Exists(lastSavedPath))
                    try { Process.Start("explorer.exe", "/select,\"" + lastSavedPath + "\""); } catch { }
            };

            // update check: first ~1 minute after start, then re-evaluated every hour (actual check at most once a day)
            updateTimer.Interval = 60 * 1000;
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 60 * 60 * 1000;
                if (S.CheckUpdates && DateTime.UtcNow.Ticks - S.LastUpdateCheck >= TimeSpan.TicksPerDay)
                    CheckForUpdates(false);
            };
            updateTimer.Start();

            RegisterHotkey();
            Settings.ApplyAutoStart(S.AutoStart);

            // a second launch of the exe (shortcut, pinned icon) signals us to take a screenshot
            var t = new Thread(() =>
            {
                while (true)
                {
                    wakeEvent.WaitOne();
                    ui.Post(_ => BeginCapture(0), null);
                }
            }) { IsBackground = true };
            t.Start();

            if (!fromAutostart && !S.StartInTray)
                ui.Post(_ => ShowSettings(null), null);
            else if (Settings.FirstRun || !hotkeyOk)
                ShowStartupBalloon();
        }

        void ShowStartupBalloon()
        {
            string msg = hotkeyOk
                ? "Running in the system tray. Capture: " + S.HotkeyText + " or click the tray icon."
                : "Could not register " + S.HotkeyText + " — it may be used by another program. Choose another one in Settings.";
            balloonUrl = null;
            tray.ShowBalloonTip(4000, "Copixy", msg, hotkeyOk ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        void RegisterHotkey()
        {
            hotkeyOk = hotkeys.Register(S.HotKey, S.HotMods, S.HotWin);
            captureItem.ShortcutKeyDisplayString = S.HotkeyText;
            tray.Text = "Copixy — " + S.HotkeyText;
        }

        void BeginCapture(int delayMs)
        {
            if (capture != null && !capture.IsDisposed)
            {
                capture.Activate();
                return;
            }
            if (settingsForm != null) return;
            if (delayMs <= 0) { DoCapture(); return; }
            // let the tray menu/tooltip disappear first
            var timer = new System.Windows.Forms.Timer { Interval = delayMs };
            timer.Tick += delegate { timer.Stop(); timer.Dispose(); DoCapture(); };
            timer.Start();
        }

        void DoCapture()
        {
            if (capture != null && !capture.IsDisposed) return;
            Rectangle vs = SystemInformation.VirtualScreen;
            Bitmap bmp;
            try
            {
                bmp = new Bitmap(vs.Width, vs.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                    Native.CopyScreen(g, vs);
            }
            catch (Exception ex)
            {
                balloonUrl = null;
                tray.ShowBalloonTip(3000, "Copixy", "Screen capture failed: " + ex.Message, ToolTipIcon.Error);
                return;
            }
            capture = new CaptureForm(S, bmp, vs, owner => ShowSettings(owner), OnSaved);
            capture.FormClosed += delegate { capture = null; };
            capture.Show();
        }

        void OnSaved(string path)
        {
            lastSavedPath = path;
            balloonUrl = null;
            tray.ShowBalloonTip(2500, "Saved", Path.GetFileName(path) + "\nClick to open the folder.", ToolTipIcon.None);
        }

        /// <summary>Asks GitHub for the latest release. manual = user clicked the menu item (always report the result).</summary>
        void CheckForUpdates(bool manual)
        {
            if (checkingUpdates) return;
            checkingUpdates = true;
            Updater.CheckAsync(r => ui.Post(_ =>
            {
                checkingUpdates = false;
                if (r.Ok)
                {
                    S.LastUpdateCheck = DateTime.UtcNow.Ticks;
                    if (r.Newer && (manual || r.Version != S.LastNotifiedVersion))
                    {
                        S.LastNotifiedVersion = r.Version;
                        balloonUrl = r.Url;
                        tray.ShowBalloonTip(8000, "Copixy " + r.Version + " is available",
                            "You have version " + AppInfo.Version + ". Click here to open the download page.", ToolTipIcon.Info);
                    }
                    else if (manual)
                    {
                        balloonUrl = null;
                        tray.ShowBalloonTip(4000, "Copixy is up to date", "You have the latest version (" + AppInfo.Version + ").", ToolTipIcon.Info);
                    }
                    S.Save();
                }
                else if (manual)
                {
                    balloonUrl = Updater.ReleasesPage;
                    tray.ShowBalloonTip(5000, "Couldn't check for updates",
                        "Check your internet connection, or click here to open the releases page.", ToolTipIcon.Warning);
                }
            }, null));
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(S.SaveFolder);
                Process.Start("explorer.exe", "\"" + S.SaveFolder + "\"");
            }
            catch { }
        }

        void ShowSettings(IWin32Window owner)
        {
            if (settingsForm != null) { settingsForm.Activate(); return; }
            hotkeys.Unregister();   // so the hotkey can be pressed in the input box
            using (settingsForm = new SettingsForm(S))
            {
                if (owner == null) settingsForm.StartPosition = FormStartPosition.CenterScreen;
                if (owner != null) { settingsForm.TopMost = true; settingsForm.ShowDialog(owner); }
                else settingsForm.ShowDialog();
            }
            settingsForm = null;
            RegisterHotkey();
            if (!hotkeyOk)
            {
                balloonUrl = null;
                tray.ShowBalloonTip(4000, "Copixy", "Hotkey " + S.HotkeyText + " is used by another program. Choose another one.", ToolTipIcon.Warning);
            }
        }

        protected override void ExitThreadCore()
        {
            if (capture != null && !capture.IsDisposed) capture.Close();
            updateTimer.Stop();
            updateTimer.Dispose();
            hotkeys.Unregister();
            hotkeys.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            S.Save();
            base.ExitThreadCore();
        }
    }

    /// <summary>Invisible window that receives the global hotkey (RegisterHotKey).</summary>
    class HotkeyWindow : NativeWindow
    {
        const int Id = 0x5C01;
        bool registered;
        public event Action Pressed;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(Keys key, Keys mods, bool win)
        {
            Unregister();
            if (key == Keys.None) return true;
            uint m = Native.MOD_NOREPEAT;
            if ((mods & Keys.Control) != 0) m |= Native.MOD_CONTROL;
            if ((mods & Keys.Shift) != 0) m |= Native.MOD_SHIFT;
            if ((mods & Keys.Alt) != 0) m |= Native.MOD_ALT;
            if (win) m |= Native.MOD_WIN;
            registered = Native.RegisterHotKey(Handle, Id, m, (uint)key);
            return registered;
        }

        public void Unregister()
        {
            if (registered) Native.UnregisterHotKey(Handle, Id);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && (int)m.WParam == Id)
            {
                if (Pressed != null) Pressed();
                return;
            }
            base.WndProc(ref m);
        }
    }
}
