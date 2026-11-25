using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Copixy
{
    class SettingsForm : Form
    {
        readonly Settings S;

        CheckBox chkAutoStart, chkTray, chkToolsMenu, chkUpdates, chkHint, chkDialog, chkNum, chkDate, chkTime;
        HotkeyBox hotkey;
        TextBox txtFolder, txtBase;
        RadioButton rbPng, rbJpg;
        TrackBar trackQ;
        Label lblQ, lblPreview;
        NumericUpDown numCounter;
        ComboBox cmbFont, cmbUiSize;
        NumericUpDown numZoom, numZoomSize;

        public SettingsForm(Settings s)
        {
            S = s;
            SuspendLayout();
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Copixy — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(484, 698);
            try { Icon = Res.AppIcon(32); } catch { }

            // ---------- General ----------
            var gGeneral = Group("General", 12, 10, 460, 218);
            chkAutoStart = Check(gGeneral, "Start with Windows", 16, 24, S.AutoStart);
            chkTray = Check(gGeneral, "Minimize to system tray on startup", 16, 48, S.StartInTray);
            chkToolsMenu = Check(gGeneral, "Hide drawing tools under a dropdown (chevron) on the toolbar", 16, 72, S.ToolsInMenu);
            chkUpdates = Check(gGeneral, "Check for updates automatically (once a day, via GitHub)", 16, 96, S.CheckUpdates);
            chkHint = Check(gGeneral, "Show the \"Select an area\" hint when a capture starts", 16, 120, S.ShowHint);
            Lbl(gGeneral, "Hotkey:", 16, 154);
            hotkey = new HotkeyBox { Location = new Point(140, 150), Width = 190 };
            hotkey.Set(S.HotKey, S.HotMods, S.HotWin);
            gGeneral.Controls.Add(hotkey);
            var hint = Lbl(gGeneral, "press new keys", 338, 154);
            Lbl(gGeneral, "Toolbar size:", 16, 188);
            cmbUiSize = new ComboBox { Location = new Point(140, 184), Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbUiSize.Items.AddRange(new object[] { "Normal", "Compact" });
            cmbUiSize.SelectedIndex = Settings.Clamp(S.UiSize, 0, 1);
            gGeneral.Controls.Add(cmbUiSize);
            hint.ForeColor = SystemColors.GrayText;

            // ---------- Saving ----------
            var gSave = Group("Saving", 12, 236, 460, 280);
            Lbl(gSave, "Folder:", 16, 30);
            txtFolder = new TextBox { Location = new Point(140, 26), Width = 222, Text = S.SaveFolder };
            gSave.Controls.Add(txtFolder);
            var btnBrowse = new Button { Text = "Browse…", Location = new Point(368, 25), Size = new Size(76, 25) };
            btnBrowse.Click += delegate { Browse(); };
            gSave.Controls.Add(btnBrowse);

            Lbl(gSave, "Default format:", 16, 64);
            rbPng = new RadioButton { Text = "PNG", Location = new Point(160, 61), AutoSize = true, Checked = S.Format != "jpg" };
            rbJpg = new RadioButton { Text = "JPG", Location = new Point(222, 61), AutoSize = true, Checked = S.Format == "jpg" };
            gSave.Controls.Add(rbPng);
            gSave.Controls.Add(rbJpg);

            Lbl(gSave, "JPG quality:", 16, 98);
            trackQ = new TrackBar { Location = new Point(132, 90), Width = 250, Minimum = 10, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10, Value = Math.Max(10, S.JpgQuality), AutoSize = false, Height = 32 };
            gSave.Controls.Add(trackQ);
            lblQ = Lbl(gSave, "", 388, 98);
            trackQ.ValueChanged += delegate { UpdateQ(); };

            chkDialog = Check(gSave, "Ask for file name when saving (Shift+click skips the dialog)", 16, 130, S.AskSaveDialog);

            Lbl(gSave, "File name:", 16, 166);
            txtBase = new TextBox { Location = new Point(140, 162), Width = 304, Text = S.FileBase };
            gSave.Controls.Add(txtBase);

            Lbl(gSave, "Append:", 16, 198);
            chkNum = Check(gSave, "number", 140, 196, S.AddNumber);
            chkDate = Check(gSave, "date", 212, 196, S.AddDate);
            chkTime = Check(gSave, "time", 274, 196, S.AddTime);
            Lbl(gSave, "next #", 342, 198);
            numCounter = new NumericUpDown { Location = new Point(394, 194), Width = 50, Minimum = 1, Maximum = 999999, Value = Math.Max(1, S.Counter) };
            gSave.Controls.Add(numCounter);

            lblPreview = Lbl(gSave, "", 16, 236);
            lblPreview.ForeColor = SystemColors.GrayText;
            lblPreview.AutoSize = false;
            lblPreview.Size = new Size(430, 34);

            EventHandler upd = delegate { UpdatePreview(); };
            txtBase.TextChanged += upd; chkNum.CheckedChanged += upd; chkDate.CheckedChanged += upd; chkTime.CheckedChanged += upd;
            rbPng.CheckedChanged += upd; numCounter.ValueChanged += upd;

            // ---------- Text ----------
            var gText = Group("Text annotations", 12, 524, 460, 60);
            Lbl(gText, "Font:", 16, 28);
            cmbFont = new ComboBox { Location = new Point(140, 24), Width = 304, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
            foreach (var f in Res.FamilyNames()) cmbFont.Items.Add(f);
            int idx = cmbFont.Items.IndexOf(S.FontFamily);
            if (idx < 0) idx = cmbFont.Items.IndexOf("Inter");
            cmbFont.SelectedIndex = idx >= 0 ? idx : (cmbFont.Items.Count > 0 ? 0 : -1);
            gText.Controls.Add(cmbFont);

            // ---------- Magnifier ----------
            var gZoom = Group("Magnifier (hold Z in capture mode)", 12, 592, 460, 60);
            Lbl(gZoom, "Zoom:", 16, 28);
            numZoom = new NumericUpDown { Location = new Point(140, 24), Width = 60, Minimum = 2, Maximum = 16, Value = Settings.Clamp(S.ZoomFactor, 2, 16) };
            gZoom.Controls.Add(numZoom);
            Lbl(gZoom, "×", 204, 28);
            Lbl(gZoom, "Diameter:", 246, 28);
            numZoomSize = new NumericUpDown { Location = new Point(316, 24), Width = 70, Minimum = 100, Maximum = 800, Increment = 10, Value = Settings.Clamp(S.ZoomSize, 100, 800) };
            gZoom.Controls.Add(numZoomSize);
            Lbl(gZoom, "px", 390, 28);

            // ---------- Buttons ----------
            var help = new LinkLabel { Text = "Help", Location = new Point(14, 668), AutoSize = true };
            help.LinkClicked += delegate { using (var f = new HelpForm()) f.ShowDialog(this); };
            Controls.Add(help);
            var about = new LinkLabel { Text = "About", Location = new Point(64, 668), AutoSize = true };
            about.LinkClicked += delegate
            {
                using (var f = new AboutForm()) f.ShowDialog(this);
                // Opened over a capture (TopMost): get out of the browser's way.
                if (AppInfo.LeaveRequested && TopMost) { DialogResult = DialogResult.Cancel; Close(); }
            };
            Controls.Add(about);
            var support = new LinkLabel { Text = "Support", Location = new Point(118, 668), AutoSize = true };
            support.LinkClicked += delegate
            {
                using (var f = new SupportForm()) f.ShowDialog(this);
                if (AppInfo.LeaveRequested && TopMost) { DialogResult = DialogResult.Cancel; Close(); }
            };
            Controls.Add(support);

            var ok = new Button { Text = "Save", Location = new Point(300, 662), Size = new Size(84, 28) };
            var cancel = new Button { Text = "Cancel", Location = new Point(388, 662), Size = new Size(84, 28), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { if (Apply()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            UpdateQ();
            UpdatePreview();
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        GroupBox Group(string text, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(g);
            return g;
        }

        static CheckBox Check(Control parent, string text, int x, int y, bool value)
        {
            var c = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true, Checked = value };
            parent.Controls.Add(c);
            return c;
        }

        static Label Lbl(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
            parent.Controls.Add(l);
            return l;
        }

        void UpdateQ() { lblQ.Text = trackQ.Value + " %"; }

        void UpdatePreview()
        {
            var tmp = S.Clone();
            tmp.FileBase = txtBase.Text;
            tmp.AddNumber = chkNum.Checked;
            tmp.AddDate = chkDate.Checked;
            tmp.AddTime = chkTime.Checked;
            numCounter.Enabled = chkNum.Checked;
            lblPreview.Text = "Example: " + tmp.BuildName(DateTime.Now, (int)numCounter.Value) + (rbJpg.Checked ? ".jpg" : ".png");
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Folder for screenshots", ShowNewFolderButton = true })
            {
                if (Directory.Exists(txtFolder.Text)) d.SelectedPath = txtFolder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = d.SelectedPath;
            }
        }

        bool Apply()
        {
            if (hotkey.Key != Keys.None && !hotkey.IsReasonable)
            {
                MessageBox.Show(this, "Add a modifier (Ctrl, Alt or Win) to a regular key,\notherwise it will stop working in other programs.",
                    "Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                hotkey.Focus();
                return false;
            }
            string folder = txtFolder.Text.Trim();
            if (folder.Length == 0) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            S.AutoStart = chkAutoStart.Checked;
            S.StartInTray = chkTray.Checked;
            S.ToolsInMenu = chkToolsMenu.Checked;
            S.CheckUpdates = chkUpdates.Checked;
            S.ShowHint = chkHint.Checked;
            S.UiSize = Math.Max(0, cmbUiSize.SelectedIndex);
            S.HotKey = hotkey.Key;
            S.HotMods = hotkey.Mods;
            S.HotWin = hotkey.Win;
            S.SaveFolder = folder;
            S.Format = rbJpg.Checked ? "jpg" : "png";
            S.JpgQuality = trackQ.Value;
            S.AskSaveDialog = chkDialog.Checked;
            S.FileBase = Settings.SanitizeName(txtBase.Text);
            S.AddNumber = chkNum.Checked;
            S.AddDate = chkDate.Checked;
            S.AddTime = chkTime.Checked;
            S.Counter = (int)numCounter.Value;
            if (cmbFont.SelectedItem != null) S.FontFamily = (string)cmbFont.SelectedItem;
            S.ZoomFactor = (int)numZoom.Value;
            S.ZoomSize = (int)numZoomSize.Value;
            S.Save();
            Settings.ApplyAutoStart(S.AutoStart);
            return true;
        }
    }

    /// <summary>Text box that records the pressed key combination.</summary>
    class HotkeyBox : TextBox
    {
        public Keys Key { get; private set; }
        public Keys Mods { get; private set; }
        public bool Win { get; private set; }

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            Cursor = Cursors.Default;
        }

        public void Set(Keys key, Keys mods, bool win)
        {
            Key = key; Mods = mods; Win = win;
            Text = Settings.HotkeyToText(key, mods, win);
        }

        public bool IsReasonable
        {
            get
            {
                if ((Mods & (Keys.Control | Keys.Alt)) != 0 || Win) return true;
                return (Key >= Keys.F1 && Key <= Keys.F24) || Key == Keys.PrintScreen || Key == Keys.Pause ||
                       Key == Keys.Scroll || Key == Keys.Insert;
            }
        }

        static bool WinDown() { return GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0; }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode, m = keyData & Keys.Modifiers;
            if ((k == Keys.Tab || k == Keys.Escape || k == Keys.Enter) && m == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
            if ((k == Keys.Back || k == Keys.Delete) && m == Keys.None) { Set(Keys.None, Keys.None, false); return true; }
            if (k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin)
            {
                string t = "";
                if (WinDown()) t += "Win + ";
                if ((m & Keys.Control) != 0) t += "Ctrl + ";
                if ((m & Keys.Shift) != 0) t += "Shift + ";
                if ((m & Keys.Alt) != 0) t += "Alt + ";
                Text = t + "…";
                return true;
            }
            Set(k, m, WinDown());
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            // PrintScreen only arrives as KeyUp
            if (e.KeyCode == Keys.PrintScreen) Set(Keys.PrintScreen, e.Modifiers, WinDown());
            else if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                if (Text.EndsWith("…") || Text.EndsWith("+ ")) Set(Key, Mods, Win);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Set(Key, Mods, Win);
        }
    }
}
