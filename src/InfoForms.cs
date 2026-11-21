using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Copixy
{
    /// <summary>Product information shown in the About window.</summary>
    static class AppInfo
    {
        public const string Name = "Copixy";
        public const string Creator = "Deformarium";
        /// <summary>Project website. Leave empty to hide the link in About.</summary>
        public const string Website = "https://github.com/Deformarium/copixy";
        public const string Donate = "https://boosty.to/deformarium/donate";

        // Set when a web link was opened from Help/About: the windows above the
        // browser (About, Settings over a capture, the capture itself) step aside.
        public static bool LeaveRequested;

        public static void OpenLink(string url, Form from)
        {
            try { Process.Start(url); } catch { return; }
            LeaveRequested = true;
            if (from != null) from.Close();
        }
        public const string Description =
            "Lightweight screenshot tool for Windows with annotations, a precision magnifier and pixel-exact area selection.";

        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                // "1.0" for x.y.0, "1.0.1" for patch releases
                return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
            }
        }
    }

    static class FormDpi
    {
        public static float Scale(Control c)
        {
            using (var g = c.CreateGraphics()) return g.DpiX / 96f;
        }
    }

    /// <summary>Keyboard and mouse shortcuts.</summary>
    class HelpForm : Form
    {
        public HelpForm()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Copixy — Help";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 600);
            MinimumSize = new Size(420, 400);
            try { Icon = Res.AppIcon(32); } catch { }

            var title = new Label
            {
                Text = "Shortcuts",
                Font = new Font("Segoe UI Semibold", 12f),
                Location = new Point(12, 10),
                AutoSize = true
            };
            Controls.Add(title);

            var list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Location = new Point(12, 42),
                Size = new Size(496, 508),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ShowGroups = true
            };
            list.Columns.Add("Action", 270);
            list.Columns.Add("Shortcut", 200);
            Controls.Add(list);
            // column widths are not DPI-scaled by WinForms: size them from the list width
            EventHandler fit = delegate
            {
                int w = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                if (w <= 0) return;
                list.Columns[0].Width = w * 52 / 100;
                list.Columns[1].Width = w - list.Columns[0].Width;
            };
            list.Resize += fit;
            Load += fit;

            Section(list, "Capture",
                "Take a screenshot", "Hotkey (PrtSc by default)",
                "Take a screenshot", "Click the tray icon",
                "Close capture", "Esc");

            Section(list, "Selecting an area",
                "Square selection", "Shift + drag",
                "Grow from the center", "Alt + drag",
                "Select the whole monitor", "Click without dragging",
                "Move the frame", "Drag inside the frame (Move)",
                "Move the frame with any tool", "Hold Space + drag",
                "Nudge the frame", "Arrow keys (Shift = 10 px)",
                "Resize the frame", "Ctrl + Arrow keys",
                "Exact size", "Type in the W / H fields, Enter");

            Section(list, "Tools",
                "Move", "M or V",
                "Line", "L",
                "Pencil", "P",
                "Rectangle", "R",
                "Ellipse", "O",
                "Text", "T",
                "Cursor", "C",
                "Eraser", "E",
                "Last drawing tool", "Click the tools icon",
                "Tools menu", "Chevron, or long-press the icon",
                "Drop the current tool", "Right-click");

            Section(list, "Drawing",
                "Dashed on / off", "D",
                "Arrowhead on / off (line)", "A",
                "Snap to 45° / square / circle", "Shift while drawing",
                "Draw from the center", "Alt while drawing",
                "Opacity 10…90 % / 100 %", "1…9 / 0",
                "Thickness, text or eraser size", "Mouse wheel",
                "Erase all drawings", "Delete",
                "Delete the hovered text or cursor", "Delete, or click ×");

            Section(list, "Text",
                "New line", "Enter",
                "Finish editing", "Esc, Ctrl+Enter, or click away",
                "Paste", "Ctrl+V",
                "Delete a word", "Ctrl+Backspace",
                "Edit existing text", "Click it",
                "Move text", "Drag it");

            Section(list, "Magnifier",
                "Show while held", "Hold Z",
                "Turn on", "Magnifier button on the toolbar",
                "Turn off", "Right-click or Esc");

            Section(list, "Copy and save",
                "Copy to clipboard", "Ctrl+C, Enter or double-click",
                "Save", "Ctrl+S",
                "Save without the dialog", "Ctrl+Shift+S or Shift+click Save",
                "Undo", "Ctrl+Z",
                "Redo", "Ctrl+Y or Ctrl+Shift+Z");

            var ok = new Button
            {
                Text = "Close",
                Size = new Size(84, 28),
                Location = new Point(424, 560),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ResumeLayout(false);
            PerformLayout();
        }

        static void Section(ListView list, string name, params string[] rows)
        {
            var g = new ListViewGroup(name);
            list.Groups.Add(g);
            for (int i = 0; i + 1 < rows.Length; i += 2)
            {
                var it = new ListViewItem(new[] { rows[i], rows[i + 1] }, g);
                list.Items.Add(it);
            }
        }
    }

    /// <summary>Short information about the program and its creator.</summary>
    class AboutForm : Form
    {
        Bitmap logo;

        public AboutForm()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "About Copixy";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            ClientSize = new Size(400, 360);
            try { Icon = Res.AppIcon(32); } catch { }

            int y = 20;
            var logoBox = new PictureBox { Location = new Point(24, y), Size = new Size(112, 64), SizeMode = PictureBoxSizeMode.Zoom };
            Controls.Add(logoBox);
            Load += delegate
            {
                // pick the logo size that matches the screen DPI, so it stays sharp
                float s = FormDpi.Scale(this);
                int need = (int)Math.Round(64 * s);
                foreach (int h in new[] { 64, 96, 128 })
                {
                    var b = Res.Icon("Copixy-Logo-" + h);
                    if (b != null) logo = b;
                    if (h >= need && b != null) break;
                }
                if (logo == null) logo = Res.Icon("Copixy-Logo-48");
                logoBox.Image = logo;
            };
            y += 64 + 14;

            Controls.Add(new Label { Text = "Version " + AppInfo.Version, Location = new Point(24, y), AutoSize = true, ForeColor = Color.FromArgb(90, 90, 100) });
            y += 28;

            Controls.Add(new Label
            {
                Text = AppInfo.Description,
                Location = new Point(24, y),
                Size = new Size(352, 40),
                ForeColor = Color.FromArgb(40, 40, 48)
            });
            y += 48;

            Controls.Add(new Label { Text = "Created by " + AppInfo.Creator, Location = new Point(24, y), AutoSize = true });
            y += 22;
            Controls.Add(new Label { Text = "© " + DateTime.Now.Year + " " + AppInfo.Creator + ". All rights reserved.", Location = new Point(24, y), AutoSize = true, ForeColor = Color.FromArgb(90, 90, 100) });
            y += 26;

            if (!string.IsNullOrEmpty(AppInfo.Website))
            {
                var link = new LinkLabel { Text = AppInfo.Website.Replace("https://", "").Replace("http://", ""), Location = new Point(24, y), AutoSize = true };
                link.LinkClicked += delegate { AppInfo.OpenLink(AppInfo.Website, this); };
                Controls.Add(link);
                y += 26;
            }

            if (!string.IsNullOrEmpty(AppInfo.Donate))
            {
                var donate = new LinkLabel { Text = "Support Copixy on Boosty", Location = new Point(24, y), AutoSize = true };
                donate.LinkClicked += delegate { AppInfo.OpenLink(AppInfo.Donate, this); };
                Controls.Add(donate);
                y += 26;
            }

            Controls.Add(new Label
            {
                Text = "Font: Inter — SIL Open Font License 1.1",
                Location = new Point(24, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 130)
            });

            var ok = new Button { Text = "OK", Size = new Size(84, 28), Location = new Point(292, 318), DialogResult = DialogResult.OK, BackColor = SystemColors.Control };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
