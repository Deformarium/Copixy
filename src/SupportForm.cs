using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Copixy
{
    /// <summary>One way to send a donation: Boosty or a crypto wallet.</summary>
    class Wallet
    {
        public string Name, Network, Address, Logo, Qr, Warning;
    }

    static class Donations
    {
        // Keep these in sync with the "Support" section of README.md,
        // so users can verify the addresses on GitHub.
        public static readonly Wallet[] Wallets =
        {
            new Wallet
            {
                Name = "Bitcoin (BTC)", Network = "Bitcoin network",
                Address = "bc1q9msyefzusdugtema6jrttt7uu3n3963s0qwkdn",
                Logo = "Bitcoin_logo", Qr = "qr-btc",
                Warning = "Send only BTC on the Bitcoin network to this address.",
            },
            new Wallet
            {
                Name = "USDT", Network = "TRON network (TRC20)",
                Address = "TSsUZhbw1hKHczPmtw62SPCKcV1nggNahJ",
                Logo = "USDT_TRC20_logo", Qr = "qr-trc20",
                Warning = "Send only USDT on the TRON network (TRC20) to this address.",
            },
            new Wallet
            {
                Name = "Solana", Network = "SOL, USDT or USDC on the Solana network",
                Address = "CWW3miZCCtF2LFaFUxA8NVG7azju3pwPTzqQSt9Lo3gm",
                Logo = "Solana_sol_logo", Qr = "qr-sol",
                Warning = "Send only SOL or Solana-network tokens (USDT, USDC) to this address.",
            },
        };

        /// <summary>High-quality resize for logos (64 px sources shown at 32 px × DPI).</summary>
        public static Bitmap Scaled(Bitmap src, int px)
        {
            if (src == null) return null;
            if (src.Width == px && src.Height == px) return src;
            var b = new Bitmap(px, px);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, px, px));
            }
            return b;
        }
    }

    /// <summary>Support window: Boosty plus crypto wallets with Copy and QR.</summary>
    class SupportForm : Form
    {
        static readonly Color Muted = Color.FromArgb(100, 100, 112);
        static readonly Color Field = Color.FromArgb(244, 245, 247);

        public SupportForm()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Support Copixy";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            try { Icon = Res.AppIcon(32); } catch { }

            const int W = 560, L = 24;
            int y = 18;

            Controls.Add(new Label { Text = "Support Copixy", Location = new Point(L, y), AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(24, 24, 32) });
            y += 34;
            Controls.Add(new Label
            {
                Text = "Copixy is free and has no ads. If it saves you time, you can support its development.",
                Location = new Point(L, y), Size = new Size(W - L * 2, 36), ForeColor = Color.FromArgb(40, 40, 48)
            });
            y += 46;

            // --- Boosty ---
            var logos = new System.Collections.Generic.List<Tuple<PictureBox, string>>();
            var bl = new PictureBox { Location = new Point(L, y), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.CenterImage };
            Controls.Add(bl); logos.Add(Tuple.Create(bl, "boosty_logo"));
            Controls.Add(new Label { Text = "Boosty", Location = new Point(L + 44, y - 1), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
            Controls.Add(new Label { Text = "One-time or monthly, by bank card", Location = new Point(L + 44, y + 17), AutoSize = true, ForeColor = Muted });
            var boosty = new Button { Text = "Open Boosty", Location = new Point(W - L - 110, y + 2), Size = new Size(110, 28), FlatStyle = FlatStyle.System };
            boosty.Click += delegate { AppInfo.OpenLink(AppInfo.Donate, this); };
            Controls.Add(boosty);
            y += 50;

            Controls.Add(new Label { Location = new Point(L, y), Size = new Size(W - L * 2, 1), BackColor = Color.FromArgb(226, 228, 233) });
            y += 14;
            Controls.Add(new Label { Text = "Crypto", Location = new Point(L, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
            y += 26;

            // --- wallets ---
            var mono = new Font("Consolas", 9.5f);
            foreach (var w in Donations.Wallets)
            {
                var wl = w;
                var pic = new PictureBox { Location = new Point(L, y), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.CenterImage };
                Controls.Add(pic); logos.Add(Tuple.Create(pic, w.Logo));
                Controls.Add(new Label { Text = w.Name, Location = new Point(L + 44, y - 1), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
                Controls.Add(new Label { Text = w.Network, Location = new Point(L + 44, y + 17), AutoSize = true, ForeColor = Muted });
                y += 40;

                var addr = new TextBox
                {
                    Text = w.Address, ReadOnly = true, Font = mono, BackColor = Field,
                    BorderStyle = BorderStyle.FixedSingle, Location = new Point(L, y + 1), Size = new Size(W - L * 2 - 150, 24),
                };
                Controls.Add(addr);

                var copy = new Button { Text = "Copy", Location = new Point(W - L - 142, y), Size = new Size(70, 27), FlatStyle = FlatStyle.System };
                var timer = new Timer { Interval = 1500 };
                timer.Tick += delegate { timer.Stop(); copy.Text = "Copy"; };
                copy.Click += delegate
                {
                    try { Clipboard.SetText(wl.Address); copy.Text = "Copied ✓"; timer.Stop(); timer.Start(); }
                    catch { MessageBox.Show(this, "Couldn't access the clipboard. Select the address and press Ctrl+C.", "Copixy"); }
                };
                Disposed += delegate { timer.Dispose(); };
                Controls.Add(copy);

                var qr = new Button { Text = "QR", Location = new Point(W - L - 66, y), Size = new Size(66, 27), FlatStyle = FlatStyle.System };
                qr.Click += delegate { using (var f = new QrForm(wl)) f.ShowDialog(this); };
                Controls.Add(qr);
                y += 46;
            }

            Controls.Add(new Label
            {
                Text = "Send each coin only on the network shown — a transfer on another network will be lost. " +
                       "After pasting, check the first and last characters of the address.",
                Location = new Point(L, y), Size = new Size(W - L * 2, 36), ForeColor = Color.FromArgb(176, 92, 0)
            });
            y += 46;

            var ok = new Button { Text = "Close", Location = new Point(W - L - 88, y), Size = new Size(88, 28), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ClientSize = new Size(W, y + 28 + 16);

            Load += delegate
            {
                int px = (int)Math.Round(32 * FormDpi.Scale(this));
                foreach (var t in logos) t.Item1.Image = Donations.Scaled(Res.Icon(t.Item2), px);
                ActiveControl = ok;   // don't start with an address selected
            };
            ResumeLayout(false);
        }
    }

    /// <summary>Large QR code of one address, for paying from a phone.</summary>
    class QrForm : Form
    {
        readonly Bitmap qr;
        readonly Panel box;

        public QrForm(Wallet w)
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = w.Name + " — QR code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            try { Icon = Res.AppIcon(32); } catch { }

            const int W = 360, L = 20;
            int y = 16;
            Controls.Add(new Label { Text = w.Name, Location = new Point(L, y), AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
            y += 24;
            Controls.Add(new Label { Text = w.Network, Location = new Point(L, y), AutoSize = true, ForeColor = Color.FromArgb(100, 100, 112) });
            y += 26;

            qr = Res.Icon(w.Qr);
            box = new Panel { Location = new Point((W - 240) / 2, y), Size = new Size(240, 240), BackColor = Color.White };
            box.Paint += PaintQr;
            Controls.Add(box);
            y += 248;

            Controls.Add(new TextBox
            {
                Text = w.Address, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = new Font("Consolas", 9f), Location = new Point(L, y), Size = new Size(W - L * 2, 34), TextAlign = HorizontalAlignment.Center,
            });
            y += 40;
            Controls.Add(new Label { Text = w.Warning, Location = new Point(L, y), Size = new Size(W - L * 2, 34), ForeColor = Color.FromArgb(176, 92, 0), TextAlign = ContentAlignment.TopCenter });
            y += 42;

            var ok = new Button { Text = "Close", Location = new Point(W - L - 88, y), Size = new Size(88, 28), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ClientSize = new Size(W, y + 28 + 14);
            Load += delegate { ActiveControl = ok; };
            ResumeLayout(false);
        }

        // Whole-pixel modules with nearest-neighbour scaling: crisp and easy to scan.
        void PaintQr(object sender, PaintEventArgs e)
        {
            if (qr == null) return;
            int area = Math.Min(box.ClientSize.Width, box.ClientSize.Height);
            int k = Math.Max(1, area / qr.Width);
            int s = qr.Width * k;
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(qr, new Rectangle((box.ClientSize.Width - s) / 2, (box.ClientSize.Height - s) / 2, s, s));
        }
    }
}
