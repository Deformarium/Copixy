using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Copixy
{
    /// <summary>Full-screen capture window: frozen image of all monitors, selection frame, toolbar, drawing.</summary>
    class CaptureForm : Form
    {
        // ---- external dependencies ----
        readonly Settings S;
        readonly Action<IWin32Window> openSettings;
        readonly Action<string> onSaved;

        // ---- image ----
        Bitmap shot, dim;
        readonly Rectangle vs;               // virtual screen (screen coordinates)

        // ---- selection ----
        Rectangle sel;
        bool hasSel;
        Rectangle lastOverlay;

        enum DragMode { None, Create, Move, Resize, Draw, MoveStamp }
        DragMode drag = DragMode.None;
        int handle = -1;
        Point dragStart, lastMouse;
        Rectangle selStart;

        // ---- drawing ----
        Tool tool = Tool.None;      // default: Move (plain screenshot)
        readonly List<Shape> shapes = new List<Shape>();
        readonly List<Shape> redo = new List<Shape>();
        Shape cur;
        TextShape editing;
        Bitmap canvas;                       // screenshot + all applied shapes (created on the first shape)
        Shape hoverObj, dragObj;
        PointF objStart;
        bool dragMoved;
        bool editingNew;
        string editOrigText;
        Bitmap Canvas { get { return canvas ?? shot; } }
        bool caretOn = true;
        readonly Timer caretTimer = new Timer { Interval = 530 };

        // ---- toolbar ----
        float scale = 1f;
        Rectangle tbMain, tbProp;
        bool tbAbove, tbInside, propOpen;
        int rowH;

        enum Kind { Icon, Field, Label, Swatch, CurColor, Value, Weight, Sep, TextButton, CursorSize, ToolMenu }

        class Item
        {
            public string Id, Icon, Tip, Text;
            public Kind Kind;
            public int Row;
            public Rectangle R;
            public Color Swatch;
        }

        readonly List<Item> items = new List<Item>();
        Item hover;
        readonly Timer tipTimer = new Timer { Interval = 450 };
        bool tipShow;
        Rectangle tipRect;

        // ---- tools menu (chevron) ----
        bool spaceHeld;           // Space held: temporary frame-move mode
        Point spaceLast;
        bool toolsMenuOpen;
        Rectangle menuRect;
        int menuHover = -1;
        Tool lastDrawTool = Tool.Line;
        bool hoverChevron;                 // mouse is over the chevron part of the tools button
        bool toolsPress;                   // tools button pressed and not yet released
        readonly Timer longPress = new Timer { Interval = 420 };

        int ChevronW { get { return Sc(18); } }

        Rectangle ChevronRect(Item it) { return new Rectangle(it.R.Right - ChevronW, it.R.Y, ChevronW, it.R.Height); }

        struct MenuEntry { public Tool T; public string Icon, Name, Key; }
        static readonly MenuEntry[] DrawTools =
        {
            new MenuEntry { T = Tool.Line,    Icon = "line",      Name = "Line",      Key = "L" },
            new MenuEntry { T = Tool.Pencil,  Icon = "pencil",    Name = "Pencil",    Key = "P" },
            new MenuEntry { T = Tool.Rect,    Icon = "square",    Name = "Rectangle", Key = "R" },
            new MenuEntry { T = Tool.Ellipse, Icon = "circle",    Name = "Ellipse",   Key = "O" },
            new MenuEntry { T = Tool.Text,    Icon = "type",      Name = "Text",      Key = "T" },
            new MenuEntry { T = Tool.Cursor,  Icon = "Cursor-Lg", Name = "Cursor",    Key = "C" },
        };

        static int DrawToolIndex(Tool t)
        {
            for (int i = 0; i < DrawTools.Length; i++) if (DrawTools[i].T == t) return i;
            return -1;
        }

        string focusField;        // "w" | "h" | null
        string fieldText = "";
        bool fieldFresh;

        // ---- magnifier (hold Z) ----
        bool zoomKey;            // Z is held
        bool zoomSticky;         // turned on by the toolbar button; right-click exits
        bool cursorHidden;
        bool ZoomActive { get { return zoomKey || zoomSticky; } }
        /// <summary>The magnifier is visible when enabled and the mouse is not over the toolbar/menu (a normal cursor is needed there).</summary>
        bool LoupeVisible
        {
            get { return ZoomActive && !OnToolbar(lastMouse) && !(toolsMenuOpen && menuRect.Contains(lastMouse)); }
        }
        int LoupeD { get { return Math.Max(S.ZoomSize, Sc(S.ZoomSize)) / 2 * 2; } }

        Rectangle hintRect;
        Rectangle badgeRect;

        static readonly Color Accent = Color.FromArgb(255, 92, 110, 255);
        static readonly Color PanelBg = Color.FromArgb(250, 30, 30, 35);
        static readonly Color[] Palette =
        {
            Color.FromArgb(0xFF, 0x3B, 0x30), Color.FromArgb(0xFF, 0x95, 0x00), Color.FromArgb(0xFF, 0xCC, 0x00),
            Color.FromArgb(0x34, 0xC7, 0x59), Color.FromArgb(0x0A, 0x84, 0xFF), Color.FromArgb(0xAF, 0x52, 0xDE),
            Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x11, 0x11, 0x11)
        };
        static int[] customColors;

        public CaptureForm(Settings settings, Bitmap screenshot, Rectangle virtualScreen,
                           Action<IWin32Window> openSettings, Action<string> onSaved)
        {
            S = settings;
            shot = screenshot;
            vs = virtualScreen;
            this.openSettings = openSettings;
            this.onSaved = onSaved;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Text = "Copixy";
            Bounds = vs;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            // dimmed copy
            dim = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dim))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(shot, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 0, 0, dim.Width, dim.Height);
            }

            EraserShape.Source = new TextureBrush(shot) { WrapMode = WrapMode.Clamp };

            longPress.Tick += delegate
            {
                longPress.Stop();
                if (!toolsPress) return;
                toolsPress = false;
                SetMenuOpen(true);
            };

            caretTimer.Tick += delegate { caretOn = !caretOn; if (editing != null) Invalidate(sel); };
            tipTimer.Tick += delegate { tipTimer.Stop(); if (hover != null && hover.Tip != null) { tipShow = true; Redraw(); } };

            Point c = Cursor.Position;
            scale = Native.ScaleAt(c) * S.UiFactor;
            UpdateHint(new Point(c.X - vs.X, c.Y - vs.Y));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
            Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            spaceHeld = false;
            zoomKey = false;
            if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            zoomKey = zoomSticky = false;
            if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            caretTimer.Dispose();
            longPress.Dispose();
            tipTimer.Dispose();
            S.Save();
            if (EraserShape.Source != null) { EraserShape.Source.Dispose(); EraserShape.Source = null; }
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (composed != null) { composed.Dispose(); composed = null; }
            if (dim != null) { dim.Dispose(); dim = null; }
            if (shot != null) { shot.Dispose(); shot = null; }
            base.OnFormClosed(e);
            GC.Collect();
        }

        int Sc(float v) { return (int)Math.Round(v * scale); }

        /// <summary>Base icon size: 24 px at 100 % (on the 48 px grid a 2 px stroke becomes exactly 1 px).</summary>
        int IconPx { get { return Math.Max(16, Sc(24)); } }

        bool ShowProp { get { return hasSel && (tool != Tool.None || propOpen); } }

        // =====================================================================
        //  Painting
        // =====================================================================

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (shot == null) return;
            var g = e.Graphics;
            Rectangle clip = e.ClipRectangle;

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);
            if (hasSel && sel.Width > 0 && sel.Height > 0)
            {
                Rectangle inter = Rectangle.Intersect(sel, clip);
                if (inter.Width > 0 && inter.Height > 0) g.DrawImage(Display, inter, inter, GraphicsUnit.Pixel);
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            if (hasSel && sel.Width > 0 && sel.Height > 0)
            {
                // shapes (clipped to the frame)
                g.SetClip(sel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (cur != null) DrawLive(g, cur);
                if (editing != null) DrawEditing(g);
                g.ResetClip();
                DrawObjectHover(g);

                DrawEraserCursor(g);
                DrawFrame(g);
                if (drag == DragMode.Create || drag == DragMode.Resize) DrawBadge(g);
                if (drag != DragMode.Create && drag != DragMode.Resize) DrawToolbar(g);
            }
            else if (drag == DragMode.None)
            {
                DrawHint(g);
            }
            DrawLoupe(g);
        }

        Rectangle LoupeRect(Point p)
        {
            int d = LoupeD, extra = Sc(72);
            int w = Math.Max(d + 12, Sc(300));
            return new Rectangle(p.X - w / 2, p.Y - d / 2 - 6 - extra, w, d + 12 + extra * 2);
        }

        /// <summary>Shows/hides the system cursor depending on whether the magnifier is visible.</summary>
        void SyncLoupe()
        {
            bool v = LoupeVisible;
            if (v == cursorHidden) return;
            if (v) Cursor.Hide(); else Cursor.Show();
            cursorHidden = v;
            Invalidate(LoupeRect(lastMouse));
        }

        void SetZoom(bool on)
        {
            if (on == zoomKey) return;
            zoomKey = on;
            SyncLoupe();
            Invalidate(LoupeRect(lastMouse));
        }

        void SetZoomSticky(bool on)
        {
            if (on == zoomSticky) return;
            zoomSticky = on;
            SyncLoupe();
            Invalidate(LoupeRect(lastMouse));
            Redraw(true);
        }

        void DrawLoupe(Graphics g)
        {
            if (!LoupeVisible || shot == null) return;
            int Z = Settings.Clamp(S.ZoomFactor, 2, 16);
            Point m = lastMouse;
            int D = LoupeD;
            int n = D / Z + 3;
            if (n % 2 == 0) n++;
            int sx0 = m.X - n / 2, sy0 = m.Y - n / 2;
            int dx0 = m.X - (n / 2) * Z - Z / 2, dy0 = m.Y - (n / 2) * Z - Z / 2;
            var circle = new Rectangle(m.X - D / 2, m.Y - D / 2, D, D);

            var state = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(circle);
                g.SetClip(clip);
                g.SmoothingMode = SmoothingMode.None;
                g.FillRectangle(Brushes.Black, circle);

                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                var src = Rectangle.Intersect(new Rectangle(sx0, sy0, n, n), new Rectangle(0, 0, shot.Width, shot.Height));
                Func<Rectangle, Rectangle> map = r => new Rectangle(dx0 + (r.X - sx0) * Z, dy0 + (r.Y - sy0) * Z, r.Width * Z, r.Height * Z);
                if (!src.IsEmpty)
                {
                    // compose the fragment 1:1 (as it looks on screen), then scale it up as a whole
                    using (var tile = new Bitmap(n, n, PixelFormat.Format32bppPArgb))
                    {
                        using (var tg = Graphics.FromImage(tile))
                        {
                            tg.Clear(Color.Black);
                            tg.CompositingMode = CompositingMode.SourceCopy;
                            var local = new Rectangle(src.X - sx0, src.Y - sy0, src.Width, src.Height);
                            tg.DrawImage(hasSel ? dim : shot, local, src, GraphicsUnit.Pixel);
                            if (hasSel)
                            {
                                var si = Rectangle.Intersect(src, sel);
                                if (!si.IsEmpty)
                                    tg.DrawImage(Display, new Rectangle(si.X - sx0, si.Y - sy0, si.Width, si.Height), si, GraphicsUnit.Pixel);
                            }
                        }
                        g.DrawImage(tile, new Rectangle(dx0, dy0, n * Z, n * Z), new Rectangle(0, 0, n, n), GraphicsUnit.Pixel);
                    }
                }
                g.PixelOffsetMode = PixelOffsetMode.Default;

                // selection frame, magnified
                if (hasSel && sel.Width > 0 && sel.Height > 0)
                {
                    var ms = map(sel);
                    using (var p = new Pen(Accent, 2f)) g.DrawRectangle(p, ms.X, ms.Y, ms.Width - 1, ms.Height - 1);
                }

                // crosshair and the pixel under the cursor
                var cp = new Rectangle(m.X - Z / 2, m.Y - Z / 2, Z, Z);
                using (var p = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
                {
                    g.DrawLine(p, circle.Left, m.Y, cp.Left - 3, m.Y);
                    g.DrawLine(p, cp.Right + 2, m.Y, circle.Right, m.Y);
                    g.DrawLine(p, m.X, circle.Top, m.X, cp.Top - 3);
                    g.DrawLine(p, m.X, cp.Bottom + 2, m.X, circle.Bottom);
                }
                using (var pb = new Pen(Color.Black, 1f)) g.DrawRectangle(pb, cp.X - 2, cp.Y - 2, cp.Width + 3, cp.Height + 3);
                using (var pw = new Pen(Color.White, 1f)) g.DrawRectangle(pw, cp.X - 1, cp.Y - 1, cp.Width + 1, cp.Height + 1);
            }
            g.Restore(state);

            // outline
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(160, 0, 0, 0), 4f)) g.DrawEllipse(p, circle);
            using (var p = new Pen(Color.White, 2f)) g.DrawEllipse(p, circle);

            // label
            string label = (hasSel && (drag == DragMode.Create || drag == DragMode.Resize))
                ? sel.Width + " × " + sel.Height
                : (m.X + vs.X) + ", " + (m.Y + vs.Y);
            int lw = (int)g.MeasureString(label, UiFont(12)).Width + Sc(18), lh = Sc(22);
            var lr = new Rectangle(m.X - lw / 2, circle.Bottom + Sc(8), lw, lh);
            if (lr.Bottom > ClientSize.Height) lr.Y = circle.Top - Sc(8) - lh;
            using (var path = RoundRect(lr, lh / 2))
            using (var b = new SolidBrush(Color.FromArgb(235, 25, 25, 30)))
                g.FillPath(b, path);
            DrawText(g, label, UiFont(12), Color.White, lr, StringAlignment.Center);

            // hint above the magnifier (only in the button-enabled mode)
            if (zoomSticky)
            {
                const string hint = "Right-click to exit magnifier";
                int hw = (int)g.MeasureString(hint, UiFont(11.5f)).Width + Sc(20), hh = Sc(22);
                var hr = new Rectangle(m.X - hw / 2, circle.Top - Sc(8) - hh, hw, hh);
                if (lr.Bottom <= circle.Top) hr.Y = lr.Top - Sc(6) - hh;          // label is already above — go above it
                if (hr.Top < 0) hr.Y = Math.Max(lr.Bottom, circle.Bottom) + Sc(6); // no room above — go below
                using (var path = RoundRect(hr, hh / 2))
                using (var b = new SolidBrush(Color.FromArgb(225, 92, 110, 255)))
                    g.FillPath(b, path);
                DrawText(g, hint, UiFont(11.5f), Color.White, hr, StringAlignment.Center);
            }
        }

        /// <summary>Preview of the shape being drawn (respecting opacity and blend mode).</summary>
        void DrawLive(Graphics g, Shape s)
        {
            if (!Compositor.NeedsLayer(s)) { s.Draw(g); return; }
            Rectangle b = Rectangle.Intersect(Compositor.Bounds(s), sel);
            if (b.Width <= 0 || b.Height <= 0) return;
            using (var tile = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb))
            {
                using (var tg = Graphics.FromImage(tile))
                {
                    tg.CompositingMode = CompositingMode.SourceCopy;
                    tg.InterpolationMode = InterpolationMode.NearestNeighbor;
                    tg.PixelOffsetMode = PixelOffsetMode.Half;
                    tg.DrawImage(Display, new Rectangle(0, 0, b.Width, b.Height), b, GraphicsUnit.Pixel);
                }
                Compositor.Apply(tile, b.Location, s, shot);
                var im = g.InterpolationMode; var po = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tile, b, new Rectangle(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
                g.InterpolationMode = im; g.PixelOffsetMode = po;
            }
        }

        // ---------- objects: cursors and text (movable, editable, deletable) ----------

        static bool IsObject(Shape x) { return x is StampShape || x is TextShape; }

        /// <summary>Objects currently visible (after the last "Erase all"), except the text being edited.</summary>
        List<Shape> VisibleObjects()
        {
            var l = new List<Shape>();
            int from = shapes.FindLastIndex(x => x is EraseAllShape) + 1;
            for (int i = from; i < shapes.Count; i++)
                if (IsObject(shapes[i]) && shapes[i] != editing) l.Add(shapes[i]);
            return l;
        }

        bool ObjectsInteractive { get { return hasSel && (tool == Tool.None || tool == Tool.Cursor || tool == Tool.Text); } }

        bool IsLive(Shape o) { return o != null && shapes.Contains(o); }

        Rectangle ObjRect(Shape o)
        {
            if (o is StampShape) return ((StampShape)o).Rect;
            var t = o as TextShape;
            if (t == null) return Rectangle.Empty;
            RectangleF b = t.Bounds(Compositor.Measure);
            b.Inflate(Sc(5), Sc(3));
            return Rectangle.Round(b);
        }

        Shape ObjectAt(Point p)
        {
            if (!ObjectsInteractive || !sel.Contains(p)) return null;
            var l = VisibleObjects();
            for (int i = l.Count - 1; i >= 0; i--)
                if (ObjRect(l[i]).Contains(p)) return l[i];
            return null;
        }

        Rectangle ObjCloseRect(Shape o)
        {
            int d = Math.Max(16, Sc(18));
            Rectangle r = ObjRect(o);
            return new Rectangle(r.Right - d / 3, r.Top - d * 2 / 3, d, d);
        }

        Rectangle ObjArea(Shape o)
        {
            return Rectangle.Inflate(Rectangle.Union(ObjRect(o), ObjCloseRect(o)), 3, 3);
        }

        void DrawObjectHover(Graphics g)
        {
            var o = drag == DragMode.MoveStamp ? null : hoverObj;
            if (o == null || !ObjectsInteractive || !IsLive(o) || o == editing) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (o is TextShape)
            {
                var r = ObjRect(o);
                using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, scale)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
            var c = ObjCloseRect(o);
            using (var b = new SolidBrush(Color.FromArgb(240, 30, 30, 35))) g.FillEllipse(b, c);
            using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, scale))) g.DrawEllipse(p, c);
            int k = c.Width / 4;
            using (var p = new Pen(Color.White, Math.Max(1.5f, scale * 1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2;
                g.DrawLine(p, cx - k, cy - k, cx + k, cy + k);
                g.DrawLine(p, cx + k, cy - k, cx - k, cy + k);
            }
        }

        void SetHoverObj(Shape o)
        {
            if (o == hoverObj) return;
            if (hoverObj != null && IsLive(hoverObj)) Invalidate(ObjArea(hoverObj));
            hoverObj = o;
            if (o != null) Invalidate(ObjArea(o));
        }

        void PlaceStamp(Point p)
        {
            var st = new StampShape { SizeIdx = S.CursorSize };
            var hs = st.HotSpot;
            st.Pos = new Point(p.X - hs.X, p.Y - hs.Y);
            shapes.Add(st);
            redo.Clear();
            MarkDirty();
            hoverObj = st;
            BeginObjDrag(st, p);
            Redraw(true);
        }

        void BeginObjDrag(Shape o, Point p)
        {
            drag = DragMode.MoveStamp;
            dragObj = o;
            dragStart = p;
            dragMoved = false;
            objStart = o is StampShape ? (PointF)((StampShape)o).Pos : ((TextShape)o).Pos;
            tipShow = false; hover = null;
        }

        void MoveObjTo(Shape o, PointF pos)
        {
            if (o is StampShape) ((StampShape)o).Pos = Point.Round(pos);
            else if (o is TextShape) ((TextShape)o).Pos = pos;
            if (o != editing) MarkDirty();
            Invalidate(Rectangle.Inflate(sel, 4, 4));
        }

        void DeleteObject(Shape o)
        {
            Invalidate(ObjArea(o));
            shapes.Remove(o);
            redo.Remove(o);
            hoverObj = null;
            MarkDirty();
            Redraw(true);
        }

        // ---------- final image: canvas + objects ----------

        Bitmap composed;
        bool composedDirty = true;

        void MarkDirty() { composedDirty = true; Invalidate(Rectangle.Inflate(sel, 4, 4)); }

        /// <summary>What is shown in the frame and goes into the file: drawings + cursors + text.</summary>
        Bitmap Display
        {
            get
            {
                var objs = VisibleObjects();
                if (objs.Count == 0) return Canvas;
                if (composed == null)
                    composed = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
                if (composedDirty)
                {
                    using (var g = Graphics.FromImage(composed))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImageUnscaled(Canvas, 0, 0);
                    }
                    foreach (var o in objs) Compositor.Apply(composed, Point.Empty, o, shot, true);
                    composedDirty = false;
                }
                return composed;
            }
        }

        void EnsureCanvas()
        {
            if (canvas != null) return;
            canvas = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(shot, 0, 0);
            }
        }

        void AddShape(Shape s)
        {
            shapes.Add(s);
            redo.Clear();
            EnsureCanvas();
            Compositor.Apply(canvas, Point.Empty, s, shot);
            MarkDirty();
        }

        void RebuildCanvas()
        {
            EnsureCanvas();
            using (var g = Graphics.FromImage(canvas))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(shot, 0, 0);
            }
            foreach (var s in shapes) Compositor.Apply(canvas, Point.Empty, s, shot);
            MarkDirty();
        }

        void DrawEditing(Graphics g)
        {
            DrawLive(g, editing);
            RectangleF b = editing.Bounds(g);
            b.Inflate(Sc(5), Sc(3));
            using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, scale)) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height);
            if (caretOn)
            {
                RectangleF c = editing.Caret(g);
                using (var br = new SolidBrush(editing.Color)) g.FillRectangle(br, c);
            }
        }

        Rectangle EraserCursorRect(Point p)
        {
            int d = S.EraserSize + 4;
            return new Rectangle(p.X - d / 2 - 2, p.Y - d / 2 - 2, d + 4, d + 4);
        }

        bool ShowEraserCursor
        {
            get { return tool == Tool.Eraser && hasSel && sel.Contains(lastMouse) && !OnToolbar(lastMouse) && (drag == DragMode.None || drag == DragMode.Draw); }
        }

        void DrawEraserCursor(Graphics g)
        {
            if (!ShowEraserCursor) return;
            float d = S.EraserSize;
            var r = new RectangleF(lastMouse.X - d / 2, lastMouse.Y - d / 2, d, d);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p1 = new Pen(Color.FromArgb(160, 0, 0, 0), 3f)) g.DrawEllipse(p1, r);
            using (var p2 = new Pen(Color.White, 1.2f)) g.DrawEllipse(p2, r);
        }

        void DrawFrame(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            int w = Math.Max(1, (int)Math.Round(scale));
            using (var p = new Pen(Accent, w) { Alignment = PenAlignment.Inset })
                g.DrawRectangle(p, sel.X, sel.Y, Math.Max(1, sel.Width), Math.Max(1, sel.Height));

            int hs = Math.Max(6, Sc(8)) / 2 * 2;
            using (var br = new SolidBrush(Color.White))
            using (var p = new Pen(Accent, w))
            {
                foreach (var pt in HandlePoints())
                {
                    var r = new Rectangle(pt.X - hs / 2, pt.Y - hs / 2, hs, hs);
                    g.FillRectangle(br, r);
                    g.DrawRectangle(p, r);
                }
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        void DrawBadge(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(badgeRect, badgeRect.Height / 2))
            using (var b = new SolidBrush(Color.FromArgb(235, 25, 25, 30)))
                g.FillPath(b, path);
            DrawText(g, sel.Width + " × " + sel.Height, UiFont(12), Color.White, badgeRect, StringAlignment.Center);
        }

        void DrawHint(Graphics g)
        {
            if (hintRect.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(hintRect, Sc(10)))
            using (var b = new SolidBrush(PanelBg))
            using (var p = new Pen(Color.FromArgb(30, 255, 255, 255)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            var r1 = new Rectangle(hintRect.X, hintRect.Y + Sc(12), hintRect.Width, Sc(22));
            var r2 = new Rectangle(hintRect.X, r1.Bottom + Sc(4), hintRect.Width, Sc(18));
            DrawText(g, "Select an area", UiFont(15, 600), Color.White, r1, StringAlignment.Center);
            DrawText(g, HintLine, UiFont(12), Color.FromArgb(170, 170, 180), r2, StringAlignment.Center);
        }

        const string HintLine = "Shift — square  ·  Alt — from center  ·  Hold Z — magnifier  ·  Click — full screen  ·  Esc — exit";

        void UpdateHint(Point p)
        {
            var scr = Screen.FromPoint(new Point(p.X + vs.X, p.Y + vs.Y));
            Rectangle mon = scr.Bounds;
            mon.Offset(-vs.X, -vs.Y);
            int w = Sc(560), h = Sc(70);
            using (var g = CreateGraphics())
                w = (int)Math.Max(w, g.MeasureString(HintLine, UiFont(12)).Width + Sc(48));
            var r = new Rectangle(mon.X + (mon.Width - w) / 2, mon.Y + (mon.Height - h) / 2, w, h);
            if (r != hintRect) { hintRect = r; Redraw(); }
        }

        // ---------- toolbar ----------

        void DrawToolbar(Graphics g)
        {
            if (tbMain.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPanel(g, tbMain);
            if (ShowProp) DrawPanel(g, tbProp);
            foreach (var it in items) DrawItem(g, it);
            DrawToolsMenu(g);
            if (tipShow && hover != null && !tipRect.IsEmpty && !toolsMenuOpen)
            {
                using (var path = RoundRect(tipRect, Sc(6)))
                using (var b = new SolidBrush(Color.FromArgb(245, 12, 12, 14)))
                    g.FillPath(b, path);
                DrawText(g, hover.Tip, UiFont(12), Color.White, tipRect, StringAlignment.Center);
            }
        }

        int MenuRowH { get { return Sc(34); } }

        Rectangle MenuRowRect(int i)
        {
            int pad = Sc(5);
            return new Rectangle(menuRect.X + pad, menuRect.Y + pad + i * MenuRowH, menuRect.Width - pad * 2, MenuRowH);
        }

        void DrawToolsMenu(Graphics g)
        {
            if (!toolsMenuOpen || menuRect.IsEmpty) return;
            DrawPanel(g, menuRect);
            int isz = IconPx;
            for (int i = 0; i < DrawTools.Length; i++)
            {
                var m = DrawTools[i];
                var r = MenuRowRect(i);
                var bg = Rectangle.Inflate(r, 0, -Sc(1));
                if (tool == m.T) FillRound(g, bg, Sc(6), Accent);
                else if (menuHover == i) FillRound(g, bg, Sc(6), Color.FromArgb(34, 255, 255, 255));
                var img = Res.IconSized(m.Icon, isz);
                if (img != null) g.DrawImageUnscaled(img, r.X + Sc(8) + (isz - img.Width) / 2, r.Y + (r.Height - img.Height) / 2);
                var tr = new Rectangle(r.X + Sc(8) + isz + Sc(10), r.Y, r.Width - isz - Sc(40), r.Height);
                DrawText(g, m.Name, UiFont(13), Color.White, tr, StringAlignment.Near);
                var kr = new Rectangle(r.Right - Sc(30), r.Y, Sc(22), r.Height);
                DrawText(g, m.Key, UiFont(12), Color.FromArgb(150, 150, 160), kr, StringAlignment.Center);
            }
        }

        void LayoutToolsMenu()
        {
            menuRect = Rectangle.Empty;
            if (!toolsMenuOpen) return;
            Item btn = items.Find(q => q.Id == "tools");
            if (btn == null) { toolsMenuOpen = false; return; }
            int pad = Sc(5);
            int w = Sc(190), h = DrawTools.Length * MenuRowH + pad * 2;
            var mon = Screen.FromPoint(new Point(btn.R.X + vs.X, btn.R.Y + vs.Y)).Bounds;
            mon.Offset(-vs.X, -vs.Y);
            int x = Math.Max(mon.Left + 2, Math.Min(btn.R.X - pad, mon.Right - w - 2));
            int below = tbMain.Bottom + Sc(6), above = tbMain.Top - Sc(6) - h;
            // open away from the frame if there is room
            int y;
            if (!tbAbove && !tbInside && below + h <= mon.Bottom) y = below;
            else if (above >= mon.Top) y = above;
            else y = Math.Min(below, mon.Bottom - h - 2);
            menuRect = new Rectangle(x, y, w, h);
        }

        void SetMenuOpen(bool open)
        {
            if (toolsMenuOpen == open) return;
            toolsMenuOpen = open;
            menuHover = -1;
            tipShow = false;
            Redraw(true);
        }

        void DrawPanel(Graphics g, Rectangle r)
        {
            int rad = Sc(9);
            for (int i = 3; i >= 1; i--)
            {
                var sr = r;
                sr.Inflate(Sc(i * 1.5f), Sc(i * 1.5f));
                sr.Offset(0, Sc(2));
                using (var path = RoundRect(sr, rad + Sc(i * 1.5f)))
                using (var b = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                    g.FillPath(b, path);
            }
            using (var path = RoundRect(r, rad))
            using (var b = new SolidBrush(PanelBg))
            using (var p = new Pen(Color.FromArgb(30, 255, 255, 255)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }

        void DrawItem(Graphics g, Item it)
        {
            Rectangle r = it.R;
            bool isHover = hover == it && drag == DragMode.None;
            switch (it.Kind)
            {
                case Kind.Sep:
                    using (var p = new Pen(Color.FromArgb(45, 255, 255, 255), Math.Max(1f, scale)))
                        g.DrawLine(p, r.X + r.Width / 2, r.Y + r.Height / 5, r.X + r.Width / 2, r.Bottom - r.Height / 5);
                    break;

                case Kind.Label:
                    DrawText(g, it.Text, UiFont(12), Color.FromArgb(160, 160, 170), r, StringAlignment.Center);
                    break;

                case Kind.Icon:
                {
                    bool active = IsActive(it.Id);
                    bool enabled = IsEnabled(it.Id);
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(2));
                    if (active) FillRound(g, bg, Sc(6), Accent);
                    else if (isHover && enabled) FillRound(g, bg, Sc(6), Color.FromArgb(34, 255, 255, 255));
                    int isz = IconPx;
                    var img = Res.IconSized(it.Icon, isz);
                    if (img != null)
                    {
                        var ir = new Rectangle(r.X + (r.Width - img.Width) / 2, r.Y + (r.Height - img.Height) / 2, img.Width, img.Height);
                        if (enabled) g.DrawImageUnscaled(img, ir.X, ir.Y);
                        else
                        {
                            using (var ia = new ImageAttributes())
                            {
                                var cm = new ColorMatrix { Matrix33 = 0.3f };
                                ia.SetColorMatrix(cm);
                                g.DrawImage(img, ir, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                            }
                        }
                    }
                    break;
                }

                case Kind.Field:
                {
                    bool focused = focusField == it.Id;
                    var fr = Rectangle.Inflate(r, -Sc(2), -Sc(4));
                    FillRound(g, fr, Sc(5), focused ? Color.FromArgb(255, 20, 20, 24) : Color.FromArgb(255, 48, 48, 56));
                    if (focused)
                        using (var path = RoundRect(fr, Sc(5)))
                        using (var p = new Pen(Accent, Math.Max(1.5f, scale * 1.5f)))
                            g.DrawPath(p, path);
                    else if (isHover)
                        using (var path = RoundRect(fr, Sc(5)))
                        using (var p = new Pen(Color.FromArgb(70, 255, 255, 255)))
                            g.DrawPath(p, path);
                    string t = focused ? fieldText : (it.Id == "w" ? sel.Width : sel.Height).ToString();
                    if (focused && fieldFresh)
                    {
                        // selected text highlight
                        var sz = g.MeasureString(fieldText, UiFont(12.5f));
                        var hr = new RectangleF(fr.X + (fr.Width - sz.Width) / 2, fr.Y + Sc(4), sz.Width, fr.Height - Sc(8));
                        using (var b = new SolidBrush(Color.FromArgb(120, Accent))) g.FillRectangle(b, hr);
                        t = fieldText;
                    }
                    DrawText(g, t, UiFont(12.5f), Color.White, fr, StringAlignment.Center);
                    if (focused && !fieldFresh)
                    {
                        float tw = t.Length > 0 ? g.MeasureString(t, UiFont(12.5f), PointF.Empty, TextShape.Format).Width : 0;
                        float cx = fr.X + fr.Width / 2f + tw / 2f + Sc(1);
                        using (var p = new Pen(Color.White, Math.Max(1f, scale)))
                            g.DrawLine(p, cx, fr.Y + Sc(6), cx, fr.Bottom - Sc(6));
                    }
                    break;
                }

                case Kind.Swatch:
                {
                    if (isHover) FillRound(g, Rectangle.Inflate(r, -Sc(2), -Sc(2)), Sc(6), Color.FromArgb(34, 255, 255, 255));
                    int d = Sc(18);
                    var cr = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
                    bool selected = S.DrawColor.ToArgb() == it.Swatch.ToArgb();
                    if (selected)
                    {
                        var rr = Rectangle.Inflate(cr, Sc(3), Sc(3));
                        using (var p = new Pen(Color.White, Math.Max(1.5f, scale * 1.5f))) g.DrawEllipse(p, rr);
                    }
                    using (var b = new SolidBrush(it.Swatch)) g.FillEllipse(b, cr);
                    using (var p = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawEllipse(p, cr);
                    break;
                }

                case Kind.CurColor:
                {
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(2));
                    if (propOpen && tool == Tool.None) FillRound(g, bg, Sc(6), Color.FromArgb(60, 255, 255, 255));
                    else if (isHover) FillRound(g, bg, Sc(6), Color.FromArgb(34, 255, 255, 255));
                    int d = Sc(18);
                    var cr = new Rectangle(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
                    using (var b = new SolidBrush(S.DrawColor)) g.FillEllipse(b, cr);
                    using (var p = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1.5f, scale * 1.5f))) g.DrawEllipse(p, cr);
                    break;
                }

                case Kind.Value:
                    DrawText(g, it.Id == "op" ? S.Opacity + " %" : (it.Id == "size" ? S.TextSize : it.Id == "er" ? S.EraserSize : S.Thickness) + " px", UiFont(12.5f, 600), Color.White, r, StringAlignment.Center);
                    break;

                case Kind.TextButton:
                {
                    bool en = IsEnabled(it.Id);
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(5));
                    FillRound(g, bg, Sc(6), isHover && en ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(34, 255, 255, 255));
                    if (it.Id == "blend")
                    {
                        var tr = new Rectangle(r.X + Sc(4), r.Y, r.Width - Sc(20), r.Height);
                        DrawText(g, Compositor.Name(S.Blend), UiFont(12.5f, 600), Color.White, tr, StringAlignment.Center);
                        int cx = r.Right - Sc(13), cy = r.Y + r.Height / 2;
                        using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1.5f, scale * 1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLines(p, new[] { new Point(cx - Sc(4), cy - Sc(2)), new Point(cx, cy + Sc(2)), new Point(cx + Sc(4), cy - Sc(2)) });
                    }
                    else
                        DrawText(g, it.Text, UiFont(12.5f, 600), en ? Color.White : Color.FromArgb(90, 255, 255, 255), r, StringAlignment.Center);
                    break;
                }

                case Kind.ToolMenu:
                {
                    bool active = DrawToolIndex(tool) >= 0;
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(2));
                    var chev = ChevronRect(it);
                    var iconPart = Rectangle.FromLTRB(bg.Left, bg.Top, chev.Left, bg.Bottom);
                    var chevPart = Rectangle.FromLTRB(chev.Left, bg.Top, bg.Right, bg.Bottom);
                    if (active) FillRound(g, bg, Sc(6), Accent);
                    if (toolsMenuOpen) FillRound(g, chevPart, Sc(6), Color.FromArgb(70, 255, 255, 255));
                    else if (isHover && hoverChevron) FillRound(g, chevPart, Sc(6), Color.FromArgb(40, 255, 255, 255));
                    if (isHover && !hoverChevron && !active) FillRound(g, iconPart, Sc(6), Color.FromArgb(34, 255, 255, 255));
                    using (var p = new Pen(Color.FromArgb(active ? 110 : 60, 255, 255, 255), Math.Max(1f, scale)))
                        g.DrawLine(p, chev.Left, r.Y + r.Height / 4, chev.Left, r.Bottom - r.Height / 4);
                    var shown = DrawTools[Math.Max(0, DrawToolIndex(active ? tool : lastDrawTool))];
                    int isz = IconPx;
                    var img = Res.IconSized(shown.Icon, isz);
                    if (img != null) g.DrawImageUnscaled(img, iconPart.X + (iconPart.Width - img.Width) / 2, r.Y + (r.Height - img.Height) / 2);
                    int cx = chev.X + chev.Width / 2, cy = r.Y + r.Height / 2;
                    using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1.5f, scale * 1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(p, new[] { new Point(cx - Sc(3), cy - Sc(2)), new Point(cx, cy + Sc(1)), new Point(cx + Sc(3), cy - Sc(2)) });
                    break;
                }

                case Kind.CursorSize:
                {
                    int idx = it.Id[2] - '0';
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(2));
                    if (S.CursorSize == idx) FillRound(g, bg, Sc(6), Accent);
                    else if (isHover) FillRound(g, bg, Sc(6), Color.FromArgb(34, 255, 255, 255));
                    int sz = Sc(12 + idx * 5);
                    var img = Res.IconSized(StampShape.Names[idx], sz);
                    if (img != null) g.DrawImageUnscaled(img, r.X + (r.Width - sz) / 2, r.Y + (r.Height - sz) / 2);
                    break;
                }

                case Kind.Weight:
                {
                    var bg = Rectangle.Inflate(r, -Sc(2), -Sc(2));
                    if (isHover) FillRound(g, bg, Sc(6), Color.FromArgb(34, 255, 255, 255));
                    var aa = new Rectangle(r.X + Sc(6), r.Y, Sc(30), r.Height);
                    DrawText(g, "Aa", Res.GetFont(S.FontFamily, 16 * scale, S.TextWeight), Color.White, aa, StringAlignment.Center);
                    var tr = new Rectangle(aa.Right + Sc(2), r.Y, r.Right - aa.Right - Sc(4), r.Height);
                    DrawText(g, Res.WeightName(S.TextWeight), UiFont(12), Color.FromArgb(170, 170, 180), tr, StringAlignment.Near);
                    break;
                }
            }
        }

        bool IsActive(string id)
        {
            if (id.StartsWith("t:")) return tool.ToString() == id.Substring(2);
            if (id == "arrow") return S.LineArrow;
            if (id == "zoom") return zoomSticky;
            if (id == "dashed") return S.Dashed;
            return false;
        }

        bool IsEnabled(string id)
        {
            if (id == "undo") return shapes.Count > 0 || (editing != null && editing.Text.Length > 0);
            if (id == "redo") return redo.Count > 0;
            if (id == "th-") return S.Thickness > 1;
            if (id == "th+") return S.Thickness < 40;
            if (id == "size-") return S.TextSize > 8;
            if (id == "size+") return S.TextSize < 200;
            if (id == "eraseall") return HasVisibleDrawings;
            if (id == "er-") return S.EraserSize > 4;
            if (id == "op-") return S.Opacity > 10;
            if (id == "op+") return S.Opacity < 100;
            if (id == "er+") return S.EraserSize < 200;
            return true;
        }

        Font UiFont(float px, int weight = 500) { return Res.GetFont("Inter", px * scale, weight); }

        static void DrawText(Graphics g, string text, Font f, Color c, Rectangle r, StringAlignment h)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None })
            using (var b = new SolidBrush(c))
            {
                sf.FormatFlags = (sf.FormatFlags | StringFormatFlags.NoWrap | StringFormatFlags.NoClip) & ~StringFormatFlags.LineLimit;
                TextShape.FillText(g, text, f, b, r, sf);
            }
        }

        static void FillRound(Graphics g, Rectangle r, int rad, Color c)
        {
            using (var path = RoundRect(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // =====================================================================
        //  Toolbar layout
        // =====================================================================

        void AddItem(int row, Kind kind, string id, int width, string icon = null, string tip = null, string text = null)
        {
            items.Add(new Item { Row = row, Kind = kind, Id = id, Icon = icon, Tip = tip, Text = text, R = new Rectangle(0, 0, width, 0) });
        }

        void BuildItems()
        {
            items.Clear();
            int B = Sc(34), sep = Sc(9);
            // --- main row ---
            AddItem(0, Kind.Field, "w", Sc(60), tip: "Width, px — type a number and press Enter");
            AddItem(0, Kind.Label, "x", Sc(14), text: "×");
            AddItem(0, Kind.Field, "h", Sc(60), tip: "Height, px — type a number and press Enter");
            AddItem(0, Kind.Sep, "", sep);
            AddItem(0, Kind.Icon, "t:None", B, "move", "Move area (M)");
            if (S.ToolsInMenu)
                AddItem(0, Kind.ToolMenu, "tools", B + Sc(18), tip: "Click — last drawing tool\nHold or ▾ — tools menu");
            else
                foreach (var m in DrawTools)
                    AddItem(0, Kind.Icon, "t:" + m.T, B, m.Icon, m.Name + " (" + m.Key + ")");
            AddItem(0, Kind.Icon, "t:Eraser", B, "eraser", "Eraser (E)");
            AddItem(0, Kind.Icon, "zoom", B, "zoom", "Magnifier (or hold Z)\nRight-click to exit");
            AddItem(0, Kind.Sep, "", sep);
            AddItem(0, Kind.Icon, "undo", B, "undo-2", "Undo (Ctrl+Z)");
            AddItem(0, Kind.Icon, "redo", B, "redo-2", "Redo (Ctrl+Y)");
            AddItem(0, Kind.Sep, "", sep);
            AddItem(0, Kind.Icon, "settings", B, "settings", "Settings");
            AddItem(0, Kind.Icon, "close", B, "x", "Close (Esc)");
            AddItem(0, Kind.Sep, "", sep);
            AddItem(0, Kind.Icon, "save", B, "save", "Save (Ctrl+S)\nShift+click — save without dialog");
            AddItem(0, Kind.Icon, "copy", B, "copy", "Copy to clipboard (Ctrl+C)");

            // --- properties row ---
            if (ShowProp && tool == Tool.Cursor)
            {
                AddItem(1, Kind.Label, "", Sc(84), text: "Cursor size");
                AddItem(1, Kind.CursorSize, "cs0", B, tip: "Small — 24 px");
                AddItem(1, Kind.CursorSize, "cs1", B, tip: "Medium — 40 px");
                AddItem(1, Kind.CursorSize, "cs2", B, tip: "Large — 64 px");
            }
            else if (ShowProp && tool == Tool.Eraser)
            {
                AddItem(1, Kind.Label, "", Sc(84), text: "Eraser size");
                AddItem(1, Kind.Icon, "er-", Sc(28), "minus", "Smaller (mouse wheel)");
                AddItem(1, Kind.Value, "er", Sc(50));
                AddItem(1, Kind.Icon, "er+", Sc(28), "plus", "Bigger (mouse wheel)");
                AddItem(1, Kind.Sep, "", sep);
                AddItem(1, Kind.TextButton, "eraseall", Sc(84), tip: "Erase all drawings (Delete)", text: "Erase all");
            }
            else if (ShowProp)
            {
                foreach (var c in Palette)
                {
                    AddItem(1, Kind.Swatch, "sw", Sc(30));
                    items[items.Count - 1].Swatch = c;
                }
                AddItem(1, Kind.Icon, "custom", B, "plus", "Custom color…");
                AddItem(1, Kind.Sep, "", sep);
                if (tool == Tool.Text)
                {
                    AddItem(1, Kind.Label, "", Sc(40), text: "Size");
                    AddItem(1, Kind.Icon, "size-", Sc(28), "minus", "Smaller (mouse wheel)");
                    AddItem(1, Kind.Value, "size", Sc(50));
                    AddItem(1, Kind.Icon, "size+", Sc(28), "plus", "Bigger (mouse wheel)");
                    AddItem(1, Kind.Sep, "", sep);
                    AddItem(1, Kind.Weight, "weight", Sc(118), tip: "Font weight (click to change)");
                    AddOpacityBlend(sep, B);
                }
                else
                {
                    AddItem(1, Kind.Label, "", Sc(74), text: "Thickness");
                    AddItem(1, Kind.Icon, "th-", Sc(28), "minus", "Thinner (mouse wheel)");
                    AddItem(1, Kind.Value, "th", Sc(46));
                    AddItem(1, Kind.Icon, "th+", Sc(28), "plus", "Thicker (mouse wheel)");
                    if (tool == Tool.Line || tool == Tool.Rect || tool == Tool.Ellipse)
                    {
                        AddItem(1, Kind.Sep, "", sep);
                        AddItem(1, Kind.Icon, "dashed", B, "dashed", "Dashed on/off (D)");
                    }
                    if (tool == Tool.Line)
                        AddItem(1, Kind.Icon, "arrow", B, "move-up-right", "Arrowhead on/off (A)");
                    AddOpacityBlend(sep, B);
                }
            }
        }

        void AddOpacityBlend(int sep, int B)
        {
            AddItem(1, Kind.Sep, "", sep);
            AddItem(1, Kind.Label, "", Sc(62), text: "Opacity");
            AddItem(1, Kind.Icon, "op-", Sc(28), "minus", "Less opaque (keys 1…0)");
            AddItem(1, Kind.Value, "op", Sc(50));
            AddItem(1, Kind.Icon, "op+", Sc(28), "plus", "More opaque (keys 1…0)");
            AddItem(1, Kind.Sep, "", sep);
            AddItem(1, Kind.TextButton, "blend", Sc(112), tip: "Blend mode");
        }

        void LayoutToolbar()
        {
            if (!hasSel || sel.Width <= 0 || sel.Height <= 0)
            {
                items.Clear(); tbMain = tbProp = Rectangle.Empty; return;
            }
            var selScr = new Rectangle(sel.X + vs.X, sel.Y + vs.Y, sel.Width, sel.Height);
            Screen scr = Screen.FromRectangle(selScr);
            Rectangle mon = scr.Bounds;
            mon.Offset(-vs.X, -vs.Y);
            scale = Native.ScaleAt(new Point(scr.Bounds.X + scr.Bounds.Width / 2, scr.Bounds.Y + scr.Bounds.Height / 2)) * S.UiFactor;

            BuildItems();
            int pad = Sc(5), gap = Sc(8), rowGap = Sc(6);
            int B = Sc(34);
            rowH = B + pad * 2;
            int[] widths = new int[2];
            foreach (var it in items) widths[it.Row] += it.R.Width;
            int mainW = widths[0] + pad * 2, propW = widths[1] + pad * 2;
            bool prop = ShowProp;
            int needH = rowH + (prop ? rowGap + rowH : 0);

            int mainY, propY;
            tbAbove = tbInside = false;
            if (sel.Bottom + gap + needH <= mon.Bottom)
            {
                mainY = sel.Bottom + gap; propY = mainY + rowH + rowGap;
            }
            else if (sel.Top - gap - needH >= mon.Top)
            {
                tbAbove = true;
                mainY = sel.Top - gap - rowH; propY = mainY - rowGap - rowH;
            }
            else
            {
                tbInside = true;
                mainY = Math.Min(sel.Bottom, mon.Bottom) - gap - rowH; propY = mainY - rowGap - rowH;
            }

            // align to the frame's right edge by default; if it doesn't fit on the left, align to the left edge
            bool right = sel.Right - mainW >= mon.Left;
            int mainX = right ? sel.Right - mainW : sel.Left;
            int propX = right ? sel.Right - propW : sel.Left;
            mainX = Math.Max(mon.Left + 2, Math.Min(mainX, mon.Right - mainW - 2));
            propX = Math.Max(mon.Left + 2, Math.Min(propX, mon.Right - propW - 2));

            tbMain = new Rectangle(mainX, mainY, mainW, rowH);
            tbProp = prop ? new Rectangle(propX, propY, propW, rowH) : Rectangle.Empty;

            int[] x = { mainX + pad, propX + pad };
            int[] y = { mainY + pad, propY + pad };
            foreach (var it in items)
            {
                it.R = new Rectangle(x[it.Row], y[it.Row], it.R.Width, B);
                x[it.Row] += it.R.Width;
            }
            if (hover != null && !items.Contains(hover)) { hover = null; tipShow = false; }
            LayoutToolsMenu();
            UpdateTipRect();
        }

        void UpdateTipRect()
        {
            tipRect = Rectangle.Empty;
            if (hover == null || hover.Tip == null) return;
            using (var g = CreateGraphics())
            {
                var sz = g.MeasureString(hover.Tip, UiFont(12));
                int w = (int)sz.Width + Sc(18), h = (int)sz.Height + Sc(10);
                int x = hover.R.X + hover.R.Width / 2 - w / 2;
                // main row: tooltip toward the frame; properties row: away from it
                bool down = hover.Row == 0 ? (tbAbove || tbInside) : !(tbAbove || tbInside);
                if (tbInside && hover.Row == 0) down = false;
                Rectangle row = hover.Row == 0 ? tbMain : tbProp;
                int y = down ? row.Bottom + Sc(6) : row.Top - Sc(6) - h;
                var mon = Screen.FromPoint(new Point(hover.R.X + vs.X, hover.R.Y + vs.Y)).Bounds;
                mon.Offset(-vs.X, -vs.Y);
                x = Math.Max(mon.Left + 2, Math.Min(x, mon.Right - w - 2));
                if (y < mon.Top) y = row.Bottom + Sc(6);
                if (y + h > mon.Bottom) y = row.Top - Sc(6) - h;
                tipRect = new Rectangle(x, y, w, h);
            }
        }

        void UpdateBadge()
        {
            int w = Sc(110), h = Sc(24);
            using (var g = CreateGraphics())
                w = (int)g.MeasureString(sel.Width + " × " + sel.Height, UiFont(12)).Width + Sc(20);
            int y = sel.Top - h - Sc(6);
            if (y < 0) y = sel.Top + Sc(6);
            badgeRect = new Rectangle(sel.Left, y, w, h);
        }

        Rectangle OverlayBounds()
        {
            Rectangle r = Rectangle.Empty;
            if (hasSel)
            {
                r = Rectangle.Inflate(sel, Sc(12), Sc(12));
                if (!tbMain.IsEmpty) r = Rectangle.Union(r, Rectangle.Inflate(tbMain, Sc(8), Sc(8)));
                if (!tbProp.IsEmpty) r = Rectangle.Union(r, Rectangle.Inflate(tbProp, Sc(8), Sc(8)));
                if (!menuRect.IsEmpty) r = Rectangle.Union(r, Rectangle.Inflate(menuRect, Sc(8), Sc(8)));
                if (!tipRect.IsEmpty) r = Rectangle.Union(r, Rectangle.Inflate(tipRect, 2, 2));
                if (!badgeRect.IsEmpty) r = Rectangle.Union(r, Rectangle.Inflate(badgeRect, 2, 2));
            }
            if (!hintRect.IsEmpty) r = r.IsEmpty ? Rectangle.Inflate(hintRect, 4, 4) : Rectangle.Union(r, Rectangle.Inflate(hintRect, 4, 4));
            return r;
        }

        /// <summary>Re-layout the toolbar and repaint the changed area.</summary>
        void Redraw(bool relayout = false)
        {
            if (relayout) LayoutToolbar();
            if (drag == DragMode.Create || drag == DragMode.Resize) UpdateBadge(); else badgeRect = Rectangle.Empty;
            Rectangle nb = OverlayBounds();
            Rectangle inv = lastOverlay.IsEmpty ? nb : (nb.IsEmpty ? lastOverlay : Rectangle.Union(lastOverlay, nb));
            if (!inv.IsEmpty) Invalidate(inv);
            lastOverlay = nb;
        }

        // =====================================================================
        //  Mouse
        // =====================================================================

        Item HitItem(Point p)
        {
            if (!hasSel || drag != DragMode.None) return null;
            if (!tbMain.Contains(p) && !tbProp.Contains(p)) return null;
            foreach (var it in items)
                if (it.R.Contains(p) && it.Kind != Kind.Sep && it.Kind != Kind.Label && it.Kind != Kind.Value) return it;
            return null;
        }

        bool OnToolbar(Point p) { return hasSel && (tbMain.Contains(p) || tbProp.Contains(p)); }

        Point[] HandlePoints()
        {
            int l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom, cx = (l + r) / 2, cy = (t + b) / 2;
            return new[] { new Point(l, t), new Point(cx, t), new Point(r, t), new Point(r, cy),
                           new Point(r, b), new Point(cx, b), new Point(l, b), new Point(l, cy) };
        }

        int HitHandle(Point p)
        {
            if (!hasSel) return -1;
            int t = Math.Max(5, Sc(7));
            var pts = HandlePoints();
            foreach (int i in new[] { 0, 2, 4, 6, 1, 3, 5, 7 })
                if (Math.Abs(p.X - pts[i].X) <= t && Math.Abs(p.Y - pts[i].Y) <= t) return i;
            int e = Math.Max(4, Sc(5));
            bool inX = p.X >= sel.Left && p.X <= sel.Right;
            bool inY = p.Y >= sel.Top && p.Y <= sel.Bottom;
            if (inX && Math.Abs(p.Y - sel.Top) <= e) return 1;
            if (inX && Math.Abs(p.Y - sel.Bottom) <= e) return 5;
            if (inY && Math.Abs(p.X - sel.Left) <= e) return 7;
            if (inY && Math.Abs(p.X - sel.Right) <= e) return 3;
            return -1;
        }

        static Cursor HandleCursor(int h)
        {
            switch (h)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        void UpdateCursor(Point p)
        {
            Cursor c;
            if (OnToolbar(p)) c = Cursors.Default;
            else if (!hasSel) c = Cursors.Cross;
            else if (spaceHeld || drag == DragMode.Move) c = Cursors.SizeAll;
            else if (ObjectsInteractive && IsLive(hoverObj) && hoverObj != editing && ObjCloseRect(hoverObj).Contains(p)) c = Cursors.Hand;
            else if (editing != null && ObjRect(editing).Contains(p)) c = Cursors.SizeAll;
            else if (ObjectsInteractive && ObjectAt(p) != null) c = Cursors.SizeAll;
            else
            {
                int h = HitHandle(p);
                if (h >= 0) c = HandleCursor(h);
                else if (sel.Contains(p))
                    c = tool == Tool.None ? Cursors.SizeAll : tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                else c = tool == Tool.None ? Cursors.Cross : Cursors.Default;
            }
            if (Cursor != c) Cursor = c;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = e.Location;
            lastMouse = p;

            if (e.Button == MouseButtons.Right)
            {
                if (drag != DragMode.None) return;
                if (toolsMenuOpen) { SetMenuOpen(false); return; }
                if (zoomSticky) { SetZoomSticky(false); return; }   // exit the magnifier; the previous tool stays active
                if (editing != null) { CommitText(); Redraw(true); return; }
                if (!hasSel) { Close(); return; }
                if (tool != Tool.None) { SetTool(Tool.None); return; }
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (toolsMenuOpen)
            {
                if (menuRect.Contains(p))
                {
                    for (int i = 0; i < DrawTools.Length; i++)
                        if (MenuRowRect(i).Contains(p)) { SetTool(DrawTools[i].T); break; }
                    return;
                }
                var hb = HitItem(p);
                if (hb == null || hb.Id != "tools") { SetMenuOpen(false); return; }   // click outside — just close
            }

            var it = HitItem(p);
            if (focusField != null && (it == null || it.Kind != Kind.Field)) ApplyField();
            if (it != null && it.Id == "tools")
            {
                tipShow = false;
                if (ChevronRect(it).Contains(p)) { SetMenuOpen(!toolsMenuOpen); return; }
                // icon: quick click selects the last tool, long press opens the menu
                if (toolsMenuOpen) SetMenuOpen(false);
                toolsPress = true;
                longPress.Stop();
                longPress.Start();
                return;
            }
            if (it != null) { ClickItem(it); return; }
            if (OnToolbar(p)) return;

            if (spaceHeld && hasSel)
            {
                // Space + drag moves the frame with any tool
                if (editing != null) CommitText();
                tipShow = false; hover = null;
                dragStart = p;
                selStart = sel;
                drag = DragMode.Move;
                return;
            }

            if (editing != null)
            {
                // drag the text while it is being typed
                if (ObjRect(editing).Contains(p)) { BeginObjDrag(editing, p); return; }
                CommitText();
                if (ObjectAt(p) == null)
                {
                    if (tool == Tool.Text && hasSel && sel.Contains(p)) StartText(p);
                    Redraw(true);
                    return;
                }
            }

            if (ObjectsInteractive)
            {
                if (IsLive(hoverObj) && ObjCloseRect(hoverObj).Contains(p)) { DeleteObject(hoverObj); return; }
                var hit = ObjectAt(p);
                if (hit != null) { BeginObjDrag(hit, p); return; }
            }

            tipShow = false; hover = null;
            dragStart = p;
            selStart = sel;

            if (!hasSel)
            {
                drag = DragMode.Create;
                hasSel = true;
                sel = new Rectangle(p, Size.Empty);
                hintRect = Rectangle.Empty;
                Redraw(true);
                return;
            }

            int h = HitHandle(p);
            if (h >= 0) { drag = DragMode.Resize; handle = h; Redraw(true); return; }

            if (sel.Contains(p))
            {
                if (tool == Tool.None) { drag = DragMode.Move; return; }
                if (tool == Tool.Text) { StartText(p); Redraw(true); return; }
                if (tool == Tool.Cursor) { PlaceStamp(p); return; }
                StartShape(p);
                return;
            }

            if (tool == Tool.None)
            {
                // new selection
                drag = DragMode.Create;
                sel = new Rectangle(p, Size.Empty);
                Redraw(true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = e.Location;
            if (ZoomActive)
            {
                Invalidate(LoupeRect(lastMouse));
                Invalidate(LoupeRect(p));
            }
            if (tool == Tool.Eraser && hasSel)
            {
                Invalidate(EraserCursorRect(lastMouse));
                Invalidate(EraserCursorRect(p));
            }
            lastMouse = p;
            if (ZoomActive) SyncLoupe();
            if (drag != DragMode.None) { DragTo(p); return; }

            if (!hasSel) { UpdateHint(p); UpdateCursor(p); return; }

            if (toolsMenuOpen)
            {
                int mh = -1;
                if (menuRect.Contains(p))
                    for (int i = 0; i < DrawTools.Length; i++) if (MenuRowRect(i).Contains(p)) mh = i;
                if (mh != menuHover) { menuHover = mh; Invalidate(Rectangle.Inflate(menuRect, 2, 2)); }
                if (menuRect.Contains(p)) { if (Cursor != Cursors.Default) Cursor = Cursors.Default; return; }
            }

            if (ObjectsInteractive && !OnToolbar(p))
            {
                if (!(IsLive(hoverObj) && ObjCloseRect(hoverObj).Contains(p)))
                    SetHoverObj(ObjectAt(p));
            }
            else SetHoverObj(null);

            var it = HitItem(p);
            bool hc = it != null && it.Id == "tools" && ChevronRect(it).Contains(p);
            if (hc != hoverChevron && it != null && it == hover) Invalidate(Rectangle.Inflate(it.R, 2, 2));
            hoverChevron = hc;
            if (it != hover)
            {
                hover = it;
                tipShow = false;
                tipTimer.Stop();
                if (it != null && it.Tip != null) tipTimer.Start();
                UpdateTipRect();
                Redraw();
            }
            UpdateCursor(p);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; tipShow = false; Redraw(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && toolsPress)
            {
                toolsPress = false;
                longPress.Stop();
                SetTool(lastDrawTool);
                return;
            }
            if (e.Button != MouseButtons.Left || drag == DragMode.None) return;
            DragMode was = drag;
            DragTo(e.Location);
            drag = DragMode.None;
            if (was == DragMode.MoveStamp)
            {
                var o = dragObj;
                dragObj = null;
                if (!dragMoved && o is TextShape && o != editing) StartEditExisting((TextShape)o);
                Redraw(true);
                UpdateCursor(e.Location);
                return;
            }

            if (was == DragMode.Create && (sel.Width < 4 || sel.Height < 4))
            {
                // click without dragging selects the whole monitor
                var scr = Screen.FromPoint(new Point(e.X + vs.X, e.Y + vs.Y));
                sel = scr.Bounds;
                sel.Offset(-vs.X, -vs.Y);
            }
            if (was == DragMode.Draw && cur != null)
            {
                bool empty = (cur is LineShape && ((LineShape)cur).IsEmpty) || (cur is BoxShape && ((BoxShape)cur).IsEmpty);
                if (!empty) AddShape(cur);
                cur = null;
            }
            Redraw(true);
            UpdateCursor(e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && hasSel && tool == Tool.None && editing == null && ObjectAt(e.Location) == null && sel.Contains(e.Location) && HitItem(e.Location) == null && HitHandle(e.Location) < 0)
                Copy();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!hasSel || (tool == Tool.None && !propOpen)) return;
            int d = e.Delta > 0 ? 1 : -1;
            if (tool == Tool.Text) ChangeTextSize(d * 2);
            else if (tool == Tool.Eraser) ChangeEraser(d * 2);
            else ChangeThickness(d);
        }

        void DragTo(Point p)
        {
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            bool alt = (ModifierKeys & Keys.Alt) != 0;
            Rectangle client = ClientRectangle;

            switch (drag)
            {
                case DragMode.Create:
                    if (spaceHeld)
                    {
                        int dx = p.X - spaceLast.X, dy = p.Y - spaceLast.Y;
                        dx = Math.Max(-sel.Left, Math.Min(dx, client.Right - sel.Right));
                        dy = Math.Max(-sel.Top, Math.Min(dy, client.Bottom - sel.Bottom));
                        dragStart.Offset(dx, dy);
                        sel.Offset(dx, dy);
                        spaceLast = new Point(spaceLast.X + dx, spaceLast.Y + dy);
                        Redraw(true);
                        break;
                    }
                    spaceLast = p;
                    sel = Rectangle.Intersect(MakeRect(dragStart, p, shift, alt), client);
                    Redraw(true);
                    break;

                case DragMode.Move:
                {
                    int x = selStart.X + p.X - dragStart.X, y = selStart.Y + p.Y - dragStart.Y;
                    x = Math.Max(0, Math.Min(x, client.Width - sel.Width));
                    y = Math.Max(0, Math.Min(y, client.Height - sel.Height));
                    sel = new Rectangle(x, y, sel.Width, sel.Height);
                    Redraw(true);
                    break;
                }

                case DragMode.Resize:
                    sel = Rectangle.Intersect(ResizeRect(selStart, handle, p.X - dragStart.X, p.Y - dragStart.Y, shift, alt), client);
                    Redraw(true);
                    break;

                case DragMode.MoveStamp:
                    if (dragObj != null)
                    {
                        int dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) dragMoved = true;
                        if (dragMoved) MoveObjTo(dragObj, new PointF(objStart.X + dx, objStart.Y + dy));
                    }
                    break;

                case DragMode.Draw:
                    UpdateShape(p, shift, alt);
                    Invalidate(Rectangle.Inflate(sel, 2, 2));
                    break;
            }
        }

        static Rectangle MakeRect(Point a, Point p, bool square, bool center)
        {
            int dx = p.X - a.X, dy = p.Y - a.Y;
            if (square)
            {
                int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                dx = (dx < 0 ? -1 : 1) * m;
                dy = (dy < 0 ? -1 : 1) * m;
            }
            if (center)
                return Rectangle.FromLTRB(a.X - Math.Abs(dx), a.Y - Math.Abs(dy), a.X + Math.Abs(dx), a.Y + Math.Abs(dy));
            return Rectangle.FromLTRB(Math.Min(a.X, a.X + dx), Math.Min(a.Y, a.Y + dy), Math.Max(a.X, a.X + dx), Math.Max(a.Y, a.Y + dy));
        }

        static Rectangle ResizeRect(Rectangle r, int h, int dx, int dy, bool shift, bool center)
        {
            bool mL = h == 0 || h == 6 || h == 7, mR = h == 2 || h == 3 || h == 4;
            bool mT = h == 0 || h == 1 || h == 2, mB = h == 4 || h == 5 || h == 6;
            float L = r.Left, T = r.Top, R = r.Right, Bt = r.Bottom;
            float cx = (L + R) / 2f, cy = (T + Bt) / 2f;
            if (mL) { L += dx; if (center) R -= dx; }
            if (mR) { R += dx; if (center) L -= dx; }
            if (mT) { T += dy; if (center) Bt -= dy; }
            if (mB) { Bt += dy; if (center) T -= dy; }

            bool corner = h == 0 || h == 2 || h == 4 || h == 6;
            if (shift && corner)
            {
                float w = R - L, hh = Bt - T;
                float m = Math.Max(Math.Abs(w), Math.Abs(hh));
                float sw = w < 0 ? -1 : 1, sh = hh < 0 ? -1 : 1;
                if (center) { L = cx - sw * m / 2; R = cx + sw * m / 2; T = cy - sh * m / 2; Bt = cy + sh * m / 2; }
                else
                {
                    if (mL) L = R - sw * m; else R = L + sw * m;
                    if (mT) T = Bt - sh * m; else Bt = T + sh * m;
                }
            }
            return Rectangle.FromLTRB((int)Math.Round(Math.Min(L, R)), (int)Math.Round(Math.Min(T, Bt)),
                                      (int)Math.Round(Math.Max(L, R)), (int)Math.Round(Math.Max(T, Bt)));
        }

        // ---------- shapes ----------

        void StartShape(Point p)
        {
            drag = DragMode.Draw;
            Color c = S.DrawColor;
            float w = S.Thickness;
            cur = null;
            switch (tool)
            {
                case Tool.Line: cur = new LineShape { A = p, B = p, Dashed = S.Dashed, Arrow = S.LineArrow, Color = c, Width = w }; break;
                case Tool.Eraser:
                    var er = new EraserShape { Width = S.EraserSize };
                    er.Points.Add(p);
                    cur = er;
                    break;
                case Tool.Rect: cur = new BoxShape { R = new RectangleF(p, SizeF.Empty), Dashed = S.Dashed, Color = c, Width = w }; break;
                case Tool.Ellipse: cur = new BoxShape { R = new RectangleF(p, SizeF.Empty), Ellipse = true, Dashed = S.Dashed, Color = c, Width = w }; break;
                case Tool.Pencil:
                    var ps = new PencilShape { Color = c, Width = w };
                    ps.Points.Add(p);
                    cur = ps;
                    break;
            }
            if (cur != null && !(cur is EraserShape)) { cur.Opacity = S.Opacity / 100f; cur.Mode = S.Blend; }
            Invalidate(sel);
        }

        void UpdateShape(Point p, bool shift, bool alt)
        {
            if (cur is LineShape)
            {
                var l = (LineShape)cur;
                PointF b = p;
                if (shift)
                {
                    double dx = p.X - l.A.X, dy = p.Y - l.A.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                    b = new PointF((float)(l.A.X + len * Math.Cos(ang)), (float)(l.A.Y + len * Math.Sin(ang)));
                }
                l.B = b;
            }
            else if (cur is BoxShape)
            {
                ((BoxShape)cur).R = MakeRect(dragStart, p, shift, alt);
            }
            else if (cur is PencilShape)
            {
                var ps = (PencilShape)cur;
                PointF last = ps.Points[ps.Points.Count - 1];
                if (Math.Abs(last.X - p.X) + Math.Abs(last.Y - p.Y) >= 1) ps.Points.Add(p);
            }
        }

        // ---------- text ----------

        void StartText(Point p)
        {
            var t = new TextShape { Color = S.DrawColor, Family = S.FontFamily, Size = S.TextSize, Weight = S.TextWeight, Width = 1,
                                    Opacity = S.Opacity / 100f, Mode = S.Blend };
            using (var g = CreateGraphics())
                t.Pos = new PointF(p.X, p.Y - t.LineHeight(g) / 2);
            editing = t;
            editingNew = true;
            editOrigText = "";
            caretOn = true;
            caretTimer.Start();
        }

        /// <summary>Opens an existing text object for editing.</summary>
        void StartEditExisting(TextShape t)
        {
            editing = t;
            editingNew = false;
            editOrigText = t.Text;
            // the toolbar shows this text's properties
            S.DrawColor = t.Color;
            S.TextSize = (int)Math.Round(t.Size);
            S.TextWeight = t.Weight;
            S.Opacity = (int)Math.Round(t.Opacity * 100);
            S.Blend = t.Mode;
            tool = Tool.Text;
            propOpen = false;
            hoverObj = null;
            caretOn = true;
            caretTimer.Start();
            MarkDirty();
            Redraw(true);
        }

        void CommitText()
        {
            if (editing == null) return;
            bool empty = editing.Text.Trim().Length == 0;
            if (editingNew)
            {
                if (!empty) { shapes.Add(editing); redo.Clear(); }
            }
            else if (empty) shapes.Remove(editing);
            editing = null;
            caretTimer.Stop();
            MarkDirty();
        }

        void CancelText()
        {
            if (editing == null) return;
            if (!editingNew) editing.Text = editOrigText;
            editing = null;
            caretTimer.Stop();
            MarkDirty();
            Redraw(true);
        }

        // =====================================================================
        //  Keyboard
        // =====================================================================

        protected override void WndProc(ref Message m)
        {
            // Alt must not activate the window's system menu
            if (m.Msg == Native.WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == Native.SC_KEYMENU) return;
            if (m.Msg == Native.WM_SYSCHAR) return;
            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) && drag != DragMode.None) DragTo(lastMouse);
            if (e.KeyCode == Keys.Menu) e.Handled = true;
            if (e.KeyCode == Keys.Z && !e.Control && editing == null && focusField == null) SetZoom(true);
            if (e.KeyCode == Keys.Space && editing == null && focusField == null && !spaceHeld)
            {
                spaceHeld = true;
                spaceLast = lastMouse;
                e.Handled = true;
                UpdateCursor(lastMouse);
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Z) SetZoom(false);
            if (e.KeyCode == Keys.Space && spaceHeld)
            {
                spaceHeld = false;
                UpdateCursor(lastMouse);
            }
            if ((e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) && drag != DragMode.None) DragTo(lastMouse);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;

            // --- size input ---
            if (focusField != null)
            {
                int digit = -1;
                if (k >= Keys.D0 && k <= Keys.D9 && !shift) digit = k - Keys.D0;
                if (k >= Keys.NumPad0 && k <= Keys.NumPad9) digit = k - Keys.NumPad0;
                if (digit >= 0)
                {
                    if (fieldFresh) fieldText = "";
                    fieldFresh = false;
                    if (fieldText.Length < 5) fieldText += digit;
                    Redraw();
                    return true;
                }
                switch (k)
                {
                    case Keys.Back:
                        if (fieldFresh) fieldText = ""; else if (fieldText.Length > 0) fieldText = fieldText.Substring(0, fieldText.Length - 1);
                        fieldFresh = false; Redraw(); return true;
                    case Keys.Enter:
                        ApplyField(); return true;
                    case Keys.Tab:
                    {
                        string next = focusField == "w" ? "h" : "w";
                        ApplyField(); FocusField(next); return true;
                    }
                    case Keys.Escape:
                        focusField = null; Redraw(); return true;
                    case Keys.Up:
                    case Keys.Down:
                    {
                        int v; int.TryParse(fieldText, out v);
                        v += (k == Keys.Up ? 1 : -1) * (shift ? 10 : 1);
                        fieldText = Math.Max(1, v).ToString(); fieldFresh = false;
                        string f = focusField; ApplyField(); FocusField(f); fieldFresh = false;
                        return true;
                    }
                }
                return true;
            }

            // --- text editing ---
            if (editing != null)
            {
                if (k == Keys.Escape || (k == Keys.Enter && ctrl)) { CommitText(); Redraw(true); return true; }
                if (k == Keys.Enter) { editing.Text += "\n"; Invalidate(sel); return true; }
                if (k == Keys.Back)
                {
                    if (editing.Text.Length > 0)
                    {
                        if (ctrl)
                        {
                            string t = editing.Text.TrimEnd(' ');
                            int i = t.LastIndexOfAny(new[] { ' ', '\n' });
                            editing.Text = i < 0 ? "" : t.Substring(0, i + 1);
                        }
                        else editing.Text = editing.Text.Substring(0, editing.Text.Length - 1);
                    }
                    Invalidate(sel); return true;
                }
                if (ctrl && k == Keys.V)
                {
                    try { if (Clipboard.ContainsText()) editing.Text += Clipboard.GetText().Replace("\r\n", "\n").Replace('\r', '\n'); } catch { }
                    Invalidate(sel); return true;
                }
                if (ctrl && k == Keys.Z) { CancelText(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (k == Keys.Escape)
            {
                if (toolsMenuOpen) { SetMenuOpen(false); return true; }
                if (zoomSticky) { SetZoomSticky(false); return true; }
                if (drag != DragMode.None) { CancelDrag(); return true; }
                Close(); return true;
            }
            if (drag != DragMode.None) return base.ProcessCmdKey(ref msg, keyData);

            if (ctrl && k == Keys.C) { Copy(); return true; }
            if (!ctrl && !alt && k == Keys.Delete && hasSel)
            {
                if (IsLive(hoverObj) && ObjectsInteractive) DeleteObject(hoverObj);
                else EraseAll();
                return true;
            }
            if (ctrl && k == Keys.S) { Save(shift); return true; }
            if (ctrl && !shift && k == Keys.Z) { Undo(); return true; }
            if (ctrl && (k == Keys.Y || (shift && k == Keys.Z))) { Redo(); return true; }
            if (!ctrl && !alt && k == Keys.Enter && hasSel) { Copy(); return true; }

            if (hasSel && !ctrl && !alt)
            {
                Tool t = Tool.None; bool isTool = true;
                switch (k)
                {
                    case Keys.L: t = Tool.Line; break;
                    case Keys.D:
                        if (tool != Tool.Line && tool != Tool.Rect && tool != Tool.Ellipse) { S.Dashed = true; SetTool(Tool.Line); }
                        else { S.Dashed = !S.Dashed; Redraw(true); }
                        return true;
                    case Keys.P: t = Tool.Pencil; break;
                    case Keys.R: t = Tool.Rect; break;
                    case Keys.O: t = Tool.Ellipse; break;
                    case Keys.T: t = Tool.Text; break;
                    case Keys.E: t = Tool.Eraser; break;
                    case Keys.C: t = Tool.Cursor; break;
                    case Keys.M: case Keys.V: SetTool(Tool.None); return true;
                    case Keys.A:
                        if (tool != Tool.Line) { S.LineArrow = true; SetTool(Tool.Line); }
                        else { S.LineArrow = !S.LineArrow; Redraw(true); }
                        return true;
                    default: isTool = false; break;
                }
                if (isTool) { SetTool(tool == t ? Tool.None : t); return true; }
            }

            if (hasSel && !ctrl && !alt && tool != Tool.None && tool != Tool.Eraser && ((k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9)))
            {
                int dgt = k >= Keys.NumPad0 ? k - Keys.NumPad0 : k - Keys.D0;
                SetOpacity(dgt == 0 ? 100 : dgt * 10);
                return true;
            }

            if (hasSel && (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down))
            {
                int step = shift ? 10 : 1;
                int dx = k == Keys.Left ? -step : k == Keys.Right ? step : 0;
                int dy = k == Keys.Up ? -step : k == Keys.Down ? step : 0;
                Rectangle c = ClientRectangle;
                if (ctrl)
                {
                    int w = Math.Max(1, Math.Min(sel.Width + dx, c.Right - sel.X));
                    int h = Math.Max(1, Math.Min(sel.Height + dy, c.Bottom - sel.Y));
                    sel = new Rectangle(sel.X, sel.Y, w, h);
                }
                else
                {
                    int x = Math.Max(0, Math.Min(sel.X + dx, c.Width - sel.Width));
                    int y = Math.Max(0, Math.Min(sel.Y + dy, c.Height - sel.Height));
                    sel = new Rectangle(x, y, sel.Width, sel.Height);
                }
                Redraw(true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (editing != null && e.KeyChar >= ' ')
            {
                editing.Text += e.KeyChar;
                caretOn = true;
                Invalidate(sel);
                e.Handled = true;
            }
        }

        void CancelDrag()
        {
            if (drag == DragMode.Draw) cur = null;
            else if (drag == DragMode.MoveStamp)
            {
                if (dragObj != null) MoveObjTo(dragObj, objStart);
                dragObj = null;
            }
            else if (drag == DragMode.Create)
            {
                if (selStart.Width > 0 && selStart.Height > 0) sel = selStart;
                else { hasSel = false; sel = Rectangle.Empty; }
            }
            else sel = selStart;
            drag = DragMode.None;
            if (!hasSel) UpdateHint(lastMouse);
            Redraw(true);
        }

        // =====================================================================
        //  Actions
        // =====================================================================

        void SetTool(Tool t)
        {
            CommitText();
            tool = t;
            if (DrawToolIndex(t) >= 0) lastDrawTool = t;
            toolsMenuOpen = false;
            menuHover = -1;
            if (t != Tool.None) propOpen = false;
            Redraw(true);
            UpdateCursor(PointToClient(Cursor.Position));
        }

        void ClickItem(Item it)
        {
            tipShow = false;
            string id = it.Id;
            if (it.Kind == Kind.Field) { FocusField(id); return; }
            if (id.StartsWith("t:"))
            {
                var t = (Tool)Enum.Parse(typeof(Tool), id.Substring(2));
                SetTool(tool == t ? Tool.None : t);
                return;
            }
            switch (id)
            {
                case "color":
                    if (tool == Tool.None) propOpen = !propOpen;
                    break;
                case "tools": SetMenuOpen(!toolsMenuOpen); return;
                case "zoom": SetZoomSticky(!zoomSticky); return;
                case "undo": Undo(); return;
                case "redo": Redo(); return;
                case "copy": Copy(); return;
                case "save": Save((ModifierKeys & Keys.Shift) != 0); return;
                case "close": Close(); return;
                case "settings":
                    CommitText();
                    openSettings(this);
                    Redraw(true);
                    Invalidate();
                    return;
                case "sw": SetColor(it.Swatch); break;
                case "custom":
                    using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = S.DrawColor })
                    {
                        if (customColors != null) dlg.CustomColors = customColors;
                        if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(dlg.Color);
                        customColors = dlg.CustomColors;
                    }
                    break;
                case "arrow": S.LineArrow = !S.LineArrow; break;
                case "dashed": S.Dashed = !S.Dashed; break;
                case "eraseall": EraseAll(); return;
                case "cs0": case "cs1": case "cs2": S.CursorSize = id[2] - '0'; break;
                case "op-": SetOpacity(S.Opacity - 10); return;
                case "op+": SetOpacity(S.Opacity + 10); return;
                case "blend": ShowBlendMenu(it); return;
                case "er-": ChangeEraser(-2); return;
                case "er+": ChangeEraser(2); return;
                case "th-": ChangeThickness(-1); return;
                case "th+": ChangeThickness(1); return;
                case "size-": ChangeTextSize(-2); return;
                case "size+": ChangeTextSize(2); return;
                case "weight":
                {
                    int[] ws = { 300, 400, 500, 600, 700 };
                    int i = Array.IndexOf(ws, S.TextWeight);
                    S.TextWeight = ws[(i + 1) % ws.Length];
                    if (editing != null) editing.Weight = S.TextWeight;
                    Invalidate(sel);
                    break;
                }
            }
            Redraw(true);
        }

        void SetColor(Color c)
        {
            S.DrawColor = Color.FromArgb(255, c);
            if (editing != null) { editing.Color = S.DrawColor; Invalidate(sel); }
            Redraw(true);
        }

        void ChangeThickness(int d)
        {
            S.Thickness = Settings.Clamp(S.Thickness + d, 1, 40);
            Redraw(true);
        }

        bool HasVisibleDrawings
        {
            get { return shapes.Count > 0 && !(shapes[shapes.Count - 1] is EraseAllShape); }
        }

        void EraseAll()
        {
            CommitText();
            if (!HasVisibleDrawings) return;
            AddShape(new EraseAllShape());
            Redraw(true);
            Invalidate(sel);
        }

        void SetOpacity(int v)
        {
            S.Opacity = Settings.Clamp(v, 10, 100);
            if (editing != null) { editing.Opacity = S.Opacity / 100f; Invalidate(sel); }
            Redraw(true);
        }

        void SetBlend(BlendMode m)
        {
            S.Blend = m;
            if (editing != null) { editing.Mode = m; Invalidate(sel); }
            Redraw(true);
        }

        void ShowBlendMenu(Item it)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            foreach (BlendMode m in Enum.GetValues(typeof(BlendMode)))
            {
                var mode = m;
                var mi = new ToolStripMenuItem(Compositor.Name(m)) { Checked = S.Blend == m };
                mi.Click += delegate { SetBlend(mode); };
                menu.Items.Add(mi);
                if (m == BlendMode.Normal || m == BlendMode.SoftLight || m == BlendMode.Lighten) menu.Items.Add(new ToolStripSeparator());
            }
            menu.Closed += delegate { BeginInvoke((Action)(() => menu.Dispose())); };
            bool up = it.Row == 1 ? (tbAbove || tbInside) : !(tbAbove || tbInside);
            var size = menu.GetPreferredSize(Size.Empty);
            Point at = up ? new Point(it.R.X, it.R.Top - size.Height - 2) : new Point(it.R.X, it.R.Bottom + 2);
            menu.Show(this, at);
        }

        void ChangeEraser(int d)
        {
            S.EraserSize = Settings.Clamp(S.EraserSize + d, 4, 200);
            Redraw(true);
            Invalidate(sel);
        }

        void ChangeTextSize(int d)
        {
            S.TextSize = Settings.Clamp(S.TextSize + d, 8, 200);
            if (editing != null) { editing.Size = S.TextSize; Invalidate(sel); }
            Redraw(true);
        }

        void FocusField(string id)
        {
            focusField = id;
            fieldText = (id == "w" ? sel.Width : sel.Height).ToString();
            fieldFresh = true;
            Redraw();
        }

        void ApplyField()
        {
            if (focusField == null) return;
            int v;
            if (int.TryParse(fieldText, out v) && v > 0)
            {
                Rectangle c = ClientRectangle;
                if (focusField == "w")
                {
                    v = Math.Min(v, c.Width);
                    int x = Math.Min(sel.X, c.Right - v);
                    sel = new Rectangle(x, sel.Y, v, sel.Height);
                }
                else
                {
                    v = Math.Min(v, c.Height);
                    int y = Math.Min(sel.Y, c.Bottom - v);
                    sel = new Rectangle(sel.X, y, sel.Width, v);
                }
            }
            focusField = null;
            Redraw(true);
        }

        void Undo()
        {
            if (editing != null) { CancelText(); return; }
            if (shapes.Count == 0) return;
            var last = shapes[shapes.Count - 1];
            redo.Add(last);
            shapes.RemoveAt(shapes.Count - 1);
            if (last == hoverObj) hoverObj = null;
            if (!IsObject(last)) RebuildCanvas(); else MarkDirty();
            Redraw(true);
            Invalidate(sel);
        }

        void Redo()
        {
            if (redo.Count == 0) return;
            CommitText();
            var rs = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            shapes.Add(rs);
            EnsureCanvas();
            Compositor.Apply(canvas, Point.Empty, rs, shot);
            MarkDirty();
            Redraw(true);
            Invalidate(sel);
        }

        Bitmap RenderSelection()
        {
            var bmp = new Bitmap(sel.Width, sel.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Display, new Rectangle(0, 0, sel.Width, sel.Height), sel, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        void Copy()
        {
            if (!hasSel || sel.Width < 1 || sel.Height < 1) return;
            CommitText();
            using (var bmp = RenderSelection())
            {
                try { Clipboard.SetDataObject(bmp, true, 10, 60); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy to clipboard:\n" + ex.Message, "Copixy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            Close();
        }

        void Save(bool quick)
        {
            if (!hasSel || sel.Width < 1 || sel.Height < 1) return;
            CommitText();

            string folder = S.SaveFolder;
            try { Directory.CreateDirectory(folder); }
            catch { folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); }

            string ext = S.Format == "jpg" ? "jpg" : "png";
            int num;
            string name = S.NextFreeName(folder, ext, out num);
            string path;

            if (S.AskSaveDialog && !quick)
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "Save screenshot";
                    dlg.InitialDirectory = folder;
                    dlg.FileName = name;
                    dlg.Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg";
                    dlg.FilterIndex = ext == "jpg" ? 2 : 1;
                    dlg.AddExtension = true;
                    dlg.DefaultExt = ext;
                    dlg.OverwritePrompt = true;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path = dlg.FileName;
                    if (dlg.FilterIndex == 2 && !IsJpg(path) && !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".jpg";
                }
            }
            else path = Path.Combine(folder, name + "." + ext);

            bool jpg = IsJpg(path);
            if (!jpg && !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { path += "." + ext; jpg = ext == "jpg"; }

            try
            {
                using (var bmp = RenderSelection())
                {
                    if (jpg)
                    {
                        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                        using (var ep = new EncoderParameters(1))
                        {
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)S.JpgQuality);
                            bmp.Save(path, codec, ep);
                        }
                    }
                    else bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the file:\n" + ex.Message, "Copixy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (S.AddNumber) S.Counter = num + 1;
            S.Save();
            if (onSaved != null) onSaved(path);
            Close();
        }

        static bool IsJpg(string p)
        {
            return p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
