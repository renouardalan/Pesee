using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pesee
{
    internal sealed class MainForm : Form
    {
        // Dimensions en pixels "logiques" (96 dpi), multipliées par l'échelle de l'écran.
        const int PadX = 6;
        const int HeaderH = 38;
        const int RowH = 32;
        const int FooterH = 32;
        const int IconSize = 16;
        const int SizeColW = 96;
        const int DefaultWidth = 400;
        const int MaxInitialHeight = 640;

        static readonly Color Bg = Color.FromArgb(243, 243, 243);
        static readonly Color HoverBg = Color.FromArgb(233, 233, 233);
        static readonly Color SelBg = Color.FromArgb(226, 226, 226);
        static readonly Color Accent = Color.FromArgb(0, 103, 192);
        static readonly Color TextMain = Color.FromArgb(26, 26, 26);
        static readonly Color TextSecondary = Color.FromArgb(100, 100, 100);
        static readonly Color TextMuted = Color.FromArgb(150, 150, 150);
        static readonly Color LineColor = Color.FromArgb(226, 226, 226);
        static readonly Color DotPending = Color.FromArgb(206, 206, 206);
        static readonly CultureInfo Fr = new CultureInfo("fr-FR");

        readonly bool showHidden, showSuperHidden;
        readonly string snapshotPath;
        readonly int threadOverride;

        string currentPath;
        readonly Stack<string> history = new Stack<string>();
        List<Entry> allEntries = new List<Entry>();
        List<Entry> rows = new List<Entry>();
        string listError;
        Scanner scan;
        long listMs;
        bool sortBySize;

        float scale = 1f;
        Font fontRow, fontHeader, fontFooter, fontIcons;
        int scrollY;
        Entry hover, selected;
        int hoverHeader = -1;               // 0 = Nom, 1 = Taille, 2 = retour, 3 = rafraîchir
        bool hoverScroll, draggingThumb;
        int dragStartY, dragStartScroll;
        Point lastMouse = new Point(-1, -1);
        string typeAhead = "";
        DateTime typeAheadTime;

        readonly System.Windows.Forms.Timer uiTimer;

        public MainForm(string path, string snapshotPath, bool startSortedBySize, int threadOverride)
        {
            this.snapshotPath = snapshotPath;
            this.threadOverride = threadOverride;
            sortBySize = startSortedBySize;
            ReadExplorerSettings(out showHidden, out showSuperHidden);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            Icon = Program.AppIcon;
            if (snapshotPath != null)
            {
                ShowInTaskbar = false;
                Location = new Point(-20000, -20000);
            }
            else
            {
                Point cur = Cursor.Position;
                Location = new Point(cur.X + 16, cur.Y - 20);
            }

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 50;
            uiTimer.Tick += OnUiTick;

            Navigate(path, false); // l'analyse démarre avant même que la fenêtre s'affiche
        }

        static void ReadExplorerSettings(out bool hidden, out bool superHidden)
        {
            hidden = false;
            superHidden = false;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (k == null) return;
                    object h = k.GetValue("Hidden");
                    object s = k.GetValue("ShowSuperHidden");
                    hidden = h is int && (int)h == 1;
                    superHidden = s is int && (int)s == 1;
                }
            }
            catch { }
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        // ------------------------------------------------------------------ cycle de vie

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetCaptionColor(Handle, Bg);
            UpdateScale();
            // Grande icône nette pour la barre des tâches et Alt+Tab, quel que soit le zoom de l'écran.
            if (Program.AppIcon != null)
            {
                int px = (int)Math.Round(32 * scale);
                bigIcon = new Icon(Program.AppIcon, new Size(px, px));
                Native.SendMessage(Handle, 0x0080 /* WM_SETICON */, new IntPtr(1) /* ICON_BIG */, bigIcon.Handle);
            }
        }

        Icon bigIcon;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            int contentH = S(HeaderH) + Math.Max(rows.Count, 4) * S(RowH) + S(FooterH) + S(4);
            ClientSize = new Size(S(DefaultWidth), Math.Min(contentH, S(MaxInitialHeight)));
            MinimumSize = new Size(S(280), S(220));
            if (snapshotPath == null)
            {
                Rectangle wa = Screen.FromPoint(Location).WorkingArea;
                int x = Math.Max(wa.Left + 8, Math.Min(Location.X, wa.Right - Width - 8));
                int y = Math.Max(wa.Top + 8, Math.Min(Location.Y, wa.Bottom - Height - 8));
                Location = new Point(x, y);
            }
        }

        // Lancée depuis le clic droit, la fenêtre doit passer devant l'Explorateur.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (snapshotPath != null) return;
            Activate();
            if (Native.GetForegroundWindow() != Handle)
            {
                TopMost = true;
                TopMost = false;
                Activate();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (scan != null) scan.Cancel();
            uiTimer.Stop();
            base.OnFormClosed(e);
        }

        void UpdateScale()
        {
            float s = Native.WindowDpi(Handle) / 96f;
            float forced;
            if (snapshotPath != null && float.TryParse(Environment.GetEnvironmentVariable("PESEE_SNAP_SCALE"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out forced)) s = forced; // tests uniquement
            if (fontRow != null && Math.Abs(s - scale) < 0.001f) return;
            scale = s;
            if (fontRow != null) { fontRow.Dispose(); fontHeader.Dispose(); fontFooter.Dispose(); }
            if (fontIcons != null) fontIcons.Dispose();
            fontRow = new Font("Segoe UI", 13.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            fontHeader = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            fontFooter = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            string iconFamily = IconFontFamily();
            fontIcons = iconFamily == null ? null : new Font(iconFamily, 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // Police d'icônes de Windows (celle des boutons de l'Explorateur) : nette à toutes les tailles.
        // Windows 11 : Segoe Fluent Icons ; Windows 10 : Segoe MDL2 Assets (mêmes codes).
        static string IconFontFamily()
        {
            foreach (string name in new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                try { using (new FontFamily(name)) return name; }
                catch (ArgumentException) { }
            }
            return null;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0;
            if (m.Msg == WM_DPICHANGED)
            {
                Native.RECT r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                UpdateScale();
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                ClampScroll();
                Invalidate();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ navigation

        void Navigate(string path, bool pushHistory)
        {
            if (scan != null) scan.Cancel();
            if (pushHistory && currentPath != null) history.Push(currentPath);
            currentPath = path;

            Stopwatch sw = Stopwatch.StartNew();
            string err;
            allEntries = Lister.List(path, showHidden, showSuperHidden, out err);
            listError = err;
            rows = new List<Entry>(allEntries.Count);
            foreach (Entry e in allEntries) if (e.Visible) rows.Add(e);
            SortRows();
            listMs = sw.ElapsedMilliseconds;

            hover = null;
            selected = null;
            scrollY = 0;
            Text = Native.DisplayName(path) ?? path;

            scan = new Scanner();
            scan.Start(allEntries, threadOverride);
            uiTimer.Start();
            Icons.LoadRealIconsAsync(rows, this);
            Invalidate();
        }

        void GoBack()
        {
            if (history.Count == 0) { GoParent(); return; }
            string from = currentPath;
            Navigate(history.Pop(), false);
            SelectByPath(from);
        }

        void GoParent()
        {
            string parent = ParentOf(currentPath);
            if (parent == null) return;
            string from = currentPath;
            Navigate(parent, true);
            SelectByPath(from);
        }

        static string ParentOf(string path)
        {
            string p = path.TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i < 0) return null;
            if (p.StartsWith(@"\\") && p.IndexOf('\\', 2) == i) return null; // \\serveur\partage
            string parent = p.Substring(0, i);
            if (parent.Length == 2 && parent[1] == ':') parent += "\\";
            return parent.Length == 0 ? null : parent;
        }

        void Rescan()
        {
            string sel = selected != null ? selected.Path : null;
            int oldScroll = scrollY;
            Navigate(currentPath, false);
            scrollY = oldScroll;
            ClampScroll();
            if (sel != null) SelectByPath(sel);
        }

        void SelectByPath(string path)
        {
            foreach (Entry e in rows)
            {
                if (string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    selected = e;
                    EnsureVisible(e);
                    break;
                }
            }
            Invalidate();
        }

        void OpenEntry(Entry e)
        {
            if (e == null) return;
            if (e.IsDir && !e.IsLink) Navigate(e.Path, true);
            else ShowInExplorer(e);
        }

        static void ShowInExplorer(Entry e)
        {
            try { Process.Start("explorer.exe", "/select,\"" + e.Path + "\""); } catch { }
        }

        static void OpenInExplorer(Entry e)
        {
            try { Process.Start("explorer.exe", "\"" + e.Path + "\""); } catch { }
        }

        // ------------------------------------------------------------------ tri et mise à jour

        void SortRows()
        {
            foreach (Entry e in rows) e.SortSize = Interlocked.Read(ref e.Size);
            if (sortBySize) rows.Sort(CompareBySize);
            else rows.Sort(CompareByName);
        }

        static int CompareByName(Entry a, Entry b)
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            return Native.StrCmpLogicalW(a.Name, b.Name);
        }

        static int CompareBySize(Entry a, Entry b)
        {
            int c = b.SortSize.CompareTo(a.SortSize);
            return c != 0 ? c : CompareByName(a, b);
        }

        void OnUiTick(object sender, EventArgs e)
        {
            bool done = scan == null || scan.Finished;
            if (sortBySize) SortRows();
            if (done)
            {
                uiTimer.Stop();
                if (snapshotPath != null) { TakeSnapshot(); return; }
            }
            if (lastMouse.X >= 0) UpdateHover(lastMouse);
            Invalidate();
        }

        // ------------------------------------------------------------------ dessin

        long MaxSize()
        {
            long max = 0;
            foreach (Entry e in rows)
            {
                long s = Interlocked.Read(ref e.Size);
                if (s > max) max = s;
            }
            return max;
        }

        long TotalSize()
        {
            long t = 0;
            foreach (Entry e in allEntries) t += Interlocked.Read(ref e.Size);
            return t;
        }

        // Dégradé vert -> jaune -> orange -> rouge selon le poids relatif au plus gros élément.
        // La racine carrée fait ressortir les éléments moyens sans écraser les petits.
        static Color SizeColor(long size, long max)
        {
            // Pendant l'analyse, une taille peut dépasser le maximum lu un instant plus tôt : on borne à [0, 1].
            double t = max <= 0 || size <= 0 ? 0 : Math.Min(1.0, Math.Sqrt((double)size / max));
            double hue = 126 * Math.Pow(1 - t, 1.6);
            return FromHsl(hue, 0.44 + 0.24 * t, 0.51);
        }

        static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hp = h / 60.0;
            double x = c * (1 - Math.Abs(hp % 2 - 1));
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; }
            else if (hp < 2) { r = x; g = c; }
            else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; }
            else if (hp < 5) { r = x; b = c; }
            else { r = c; b = x; }
            double m = l - c / 2;
            return Color.FromArgb(Channel(r + m), Channel(g + m), Channel(b + m));
        }

        static int Channel(double v)
        {
            if (double.IsNaN(v)) return 0;
            return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(Fr) + " o";
            string[] units = { "o", "Ko", "Mo", "Go", "To", "Po" };
            double v = bytes;
            int u = 0;
            while (v >= 1000 && u < units.Length - 1) { v /= 1024; u++; }
            return (v < 10 ? v.ToString("0.#", Fr) : v.ToString("0", Fr)) + " " + units[u];
        }

        static string FormatCount(long n, string singular, string plural)
        {
            return n.ToString("N0", Fr) + " " + (n > 1 ? plural : singular);
        }

        struct LayoutInfo
        {
            public int W, H, ListTop, ListBottom, RowH, RowLeft, RowRight, IconX, NameX, SizeX, IconPx;
        }

        LayoutInfo GetLayout()
        {
            LayoutInfo l = new LayoutInfo();
            l.W = ClientSize.Width;
            l.H = ClientSize.Height;
            l.ListTop = S(HeaderH);
            l.ListBottom = l.H - S(FooterH);
            l.RowH = S(RowH);
            l.RowLeft = S(PadX);
            l.RowRight = l.W - S(PadX);
            l.IconPx = S(IconSize);
            l.IconX = l.RowLeft + S(12);
            l.NameX = l.IconX + l.IconPx + S(10);
            l.SizeX = l.RowRight - S(SizeColW);
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Un souci de dessin ne doit jamais bloquer la fenêtre (croix rouge) : on redessine au rafraîchissement suivant.
            try { Render(e.Graphics); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }

        void Render(Graphics g)
        {
            LayoutInfo l = GetLayout();
            g.Clear(Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawHeader(g, l);

            g.SetClip(new Rectangle(0, l.ListTop, l.W, l.ListBottom - l.ListTop));
            long max = MaxSize();
            int first = Math.Max(0, scrollY / l.RowH);
            for (int i = first; i < rows.Count; i++)
            {
                int y = l.ListTop + i * l.RowH - scrollY;
                if (y >= l.ListBottom) break;
                DrawRow(g, l, rows[i], y, max);
            }
            if (rows.Count == 0)
            {
                string msg = listError ?? "Ce dossier est vide.";
                TextRenderer.DrawText(g, msg, fontRow, new Rectangle(0, l.ListTop, l.W, S(RowH) * 3), TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            g.ResetClip();

            DrawScrollbar(g, l);
            DrawFooter(g, l);
        }

        bool CanGoUp { get { return history.Count > 0 || ParentOf(currentPath) != null; } }

        // En-tête :  [←] [⟳]  Nom ^  ......  Taille
        Rectangle BackButtonRect(LayoutInfo l)
        {
            int b = S(28);
            return new Rectangle(l.RowLeft + S(4), (l.ListTop - b) / 2, b, b);
        }

        Rectangle RefreshButtonRect(LayoutInfo l)
        {
            Rectangle back = BackButtonRect(l);
            return new Rectangle(back.Right + S(2), back.Y, back.Width, back.Height);
        }

        int NameLabelX(LayoutInfo l) { return RefreshButtonRect(l).Right + S(10); }

        void DrawHeader(Graphics g, LayoutInfo l)
        {
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            // Retour (grisé quand il n'y a nulle part où revenir), puis Rafraîchir
            bool canBack = CanGoUp;
            DrawIconButton(g, BackButtonRect(l), "", canBack && hoverHeader == 2,
                !canBack ? DotPending : hoverHeader == 2 ? TextMain : TextSecondary);
            DrawIconButton(g, RefreshButtonRect(l), "", hoverHeader == 3,
                hoverHeader == 3 ? TextMain : TextSecondary);

            DrawHeaderLabel(g, "Nom", NameLabelX(l), l, hoverHeader == 0, !sortBySize, true, f);
            DrawHeaderLabel(g, "Taille", l.SizeX, l, hoverHeader == 1, sortBySize, false, f);
        }

        void DrawIconButton(Graphics g, Rectangle r, string glyph, bool hot, Color color)
        {
            if (hot) FillRounded(g, r, S(4), HoverBg);
            if (fontIcons != null)
            {
                TextRenderer.DrawText(g, glyph, fontIcons, r, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                return;
            }
            // Sans police d'icônes (rare) : simple flèche ou cercle tracés.
            using (Pen p = new Pen(color, Math.Max(1.2f, 1.4f * scale)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, a = 4.5f * scale;
                if (glyph == "")
                {
                    g.DrawLine(p, cx - a, cy, cx + a, cy);
                    g.DrawLine(p, cx - a, cy, cx - a + 3.5f * scale, cy - 3.5f * scale);
                    g.DrawLine(p, cx - a, cy, cx - a + 3.5f * scale, cy + 3.5f * scale);
                }
                else g.DrawArc(p, cx - a, cy - a, 2 * a, 2 * a, 10, 290);
            }
        }

        void DrawHeaderLabel(Graphics g, string text, int x, LayoutInfo l, bool hot, bool active, bool up, TextFormatFlags f)
        {
            Color c = hot || active ? TextMain : TextSecondary;
            TextRenderer.DrawText(g, text, fontHeader, new Rectangle(x, 0, S(120), l.ListTop), c, f);
            if (!active) return;
            int tw = TextRenderer.MeasureText(g, text, fontHeader, Size.Empty, f).Width;
            float cx = x + tw + 4 * scale, cy = l.ListTop / 2f + 0.5f * scale, a = 3f * scale;
            using (Pen p = new Pen(TextSecondary, Math.Max(1f, 1.2f * scale)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                float d = up ? -a / 2 : a / 2;
                g.DrawLine(p, cx, cy - d, cx + a, cy + d);
                g.DrawLine(p, cx + a, cy + d, cx + 2 * a, cy - d);
            }
        }

        void DrawRow(Graphics g, LayoutInfo l, Entry e, int y, long max)
        {
            Rectangle rr = new Rectangle(l.RowLeft, y + S(1), l.RowRight - l.RowLeft, l.RowH - S(2));
            if (e == selected)
            {
                FillRounded(g, rr, S(4), SelBg);
                int ph = S(16);
                FillRounded(g, new Rectangle(l.RowLeft, y + (l.RowH - ph) / 2, Math.Max(2, S(3)), ph), Math.Max(1, S(1.5f)), Accent);
            }
            else if (e == hover)
            {
                FillRounded(g, rr, S(4), HoverBg);
            }

            bool faded = (e.Attributes & Native.FILE_ATTRIBUTE_HIDDEN) != 0;
            Bitmap icon = Icons.For(e, l.IconPx > 16);
            if (icon != null)
            {
                Rectangle ir = new Rectangle(l.IconX, y + (l.RowH - l.IconPx) / 2, l.IconPx, l.IconPx);
                if (faded)
                {
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix33 = 0.5f;
                        ia.SetColorMatrix(cm);
                        g.DrawImage(icon, ir, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else g.DrawImage(icon, ir);
            }

            // PreserveGraphicsClipping : le texte reste coupé à la zone de la liste, comme l'icône et la pastille.
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                TextFormatFlags.PreserveGraphicsClipping;
            TextRenderer.DrawText(g, e.Name, fontRow, new Rectangle(l.NameX, y, l.SizeX - l.NameX - S(10), l.RowH),
                faded ? TextMuted : TextMain, f | TextFormatFlags.EndEllipsis);

            long size = Interlocked.Read(ref e.Size);
            bool done = e.Done;
            float d = 8f * scale;
            using (SolidBrush b = new SolidBrush(done && !e.IsLink ? SizeColor(size, max) : DotPending))
                g.FillEllipse(b, l.SizeX, y + l.RowH / 2f - d / 2f, d, d);

            string txt = e.IsLink ? "lien" : FormatSize(size);
            TextRenderer.DrawText(g, txt, fontRow, new Rectangle(l.SizeX + S(16), y, l.RowRight - l.SizeX - S(16), l.RowH),
                done && !e.IsLink ? TextMain : TextMuted, f);
        }

        void DrawFooter(Graphics g, LayoutInfo l)
        {
            int top = l.ListBottom;
            using (SolidBrush b = new SolidBrush(Bg)) g.FillRectangle(b, 0, top, l.W, l.H - top);
            using (Pen p = new Pen(LineColor)) g.DrawLine(p, 0, top, l.W, top);
            TextRenderer.DrawText(g, FooterText(), fontFooter,
                new Rectangle(l.IconX, top, l.W - l.IconX - S(12), l.H - top), TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        string FooterText()
        {
            Entry e = hover ?? selected;
            if (e != null)
            {
                StringBuilder sb = new StringBuilder(e.Name);
                if (e.IsLink) return sb.Append("  ·  lien vers un autre emplacement (non compté)").ToString();
                sb.Append("  ·  ").Append(FormatSize(Interlocked.Read(ref e.Size)));
                if (e.IsDir)
                {
                    sb.Append("  ·  ").Append(FormatCount(Interlocked.Read(ref e.Files), "fichier", "fichiers"));
                    sb.Append("  ·  ").Append(FormatCount(Interlocked.Read(ref e.Dirs), "dossier", "dossiers"));
                    if (e.Errors > 0) sb.Append("  ·  ").Append(FormatCount(e.Errors, "accès refusé", "accès refusés"));
                    if (!e.Done) sb.Append("  ·  calcul…");
                }
                return sb.ToString();
            }
            if (listError != null) return currentPath;
            if (scan != null && !scan.Finished)
                return "Analyse en cours…  " + FormatCount(scan.DirsScanned, "dossier parcouru", "dossiers parcourus");

            int errors = 0;
            foreach (Entry x in rows) errors += x.Errors;
            string s = FormatCount(rows.Count, "élément", "éléments") + "  ·  " + FormatSize(TotalSize()) + " au total";
            if (errors > 0) s += "  ·  " + FormatCount(errors, "dossier inaccessible", "dossiers inaccessibles");
            return s;
        }

        void ScrollMetrics(LayoutInfo l, out int contentH, out int listH, out Rectangle thumb)
        {
            contentH = rows.Count * l.RowH;
            listH = l.ListBottom - l.ListTop;
            thumb = Rectangle.Empty;
            if (contentH <= listH) return;
            int thumbH = Math.Max(S(28), (int)((long)listH * listH / contentH));
            int maxScroll = contentH - listH;
            int thumbY = l.ListTop + (int)((long)(listH - thumbH) * scrollY / maxScroll);
            int tw = hoverScroll || draggingThumb ? S(6) : S(4);
            thumb = new Rectangle(l.W - S(4) - tw, thumbY + S(2), tw, thumbH - S(4));
        }

        void DrawScrollbar(Graphics g, LayoutInfo l)
        {
            int contentH, listH;
            Rectangle thumb;
            ScrollMetrics(l, out contentH, out listH, out thumb);
            if (thumb.IsEmpty) return;
            FillRounded(g, thumb, thumb.Width / 2f, Color.FromArgb(hoverScroll || draggingThumb ? 120 : 80, 0, 0, 0));
        }

        static void FillRounded(Graphics g, RectangleF r, float radius, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                if (radius <= 0.5f) { g.FillRectangle(b, r); return; }
                float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddArc(r.X, r.Y, d, d, 180, 90);
                    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    p.CloseFigure();
                    g.FillPath(b, p);
                }
            }
        }

        // ------------------------------------------------------------------ souris

        Entry RowAt(Point pt)
        {
            LayoutInfo l = GetLayout();
            if (pt.Y < l.ListTop || pt.Y >= l.ListBottom || pt.X < 0 || pt.X >= l.W) return null;
            int i = (pt.Y - l.ListTop + scrollY) / l.RowH;
            return i >= 0 && i < rows.Count ? rows[i] : null;
        }

        int HeaderAt(Point pt)
        {
            LayoutInfo l = GetLayout();
            if (pt.Y < 0 || pt.Y >= l.ListTop) return -1;
            if (BackButtonRect(l).Contains(pt)) return CanGoUp ? 2 : -1;
            if (RefreshButtonRect(l).Contains(pt)) return 3;
            if (pt.X >= l.SizeX - S(8)) return 1;
            if (pt.X >= NameLabelX(l) - S(8)) return 0;
            return -1;
        }

        bool InScrollZone(Point pt)
        {
            LayoutInfo l = GetLayout();
            int contentH, listH;
            Rectangle thumb;
            ScrollMetrics(l, out contentH, out listH, out thumb);
            return !thumb.IsEmpty && pt.X >= l.W - S(14) && pt.Y >= l.ListTop && pt.Y < l.ListBottom;
        }

        void UpdateHover(Point pt)
        {
            Entry h = InScrollZone(pt) ? null : RowAt(pt);
            int hh = HeaderAt(pt);
            bool hs = InScrollZone(pt);
            if (h != hover || hh != hoverHeader || hs != hoverScroll)
            {
                hover = h;
                hoverHeader = hh;
                hoverScroll = hs;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMouse = e.Location;
            if (draggingThumb)
            {
                LayoutInfo l = GetLayout();
                int contentH, listH;
                Rectangle thumb;
                ScrollMetrics(l, out contentH, out listH, out thumb);
                if (thumb.IsEmpty) return;
                int thumbH = thumb.Height + S(4);
                float ratio = (float)(contentH - listH) / Math.Max(1, listH - thumbH);
                scrollY = dragStartScroll + (int)((e.Y - dragStartY) * ratio);
                ClampScroll();
                Invalidate();
                return;
            }
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            lastMouse = new Point(-1, -1);
            if (!draggingThumb && (hover != null || hoverHeader != -1 || hoverScroll))
            {
                hover = null;
                hoverHeader = -1;
                hoverScroll = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.XButton1) { GoBack(); return; }
            if (e.Button == MouseButtons.Left && InScrollZone(e.Location))
            {
                LayoutInfo l = GetLayout();
                int contentH, listH;
                Rectangle thumb;
                ScrollMetrics(l, out contentH, out listH, out thumb);
                if (e.Y < thumb.Top) scrollY -= listH;
                else if (e.Y > thumb.Bottom) scrollY += listH;
                else
                {
                    draggingThumb = true;
                    dragStartY = e.Y;
                    dragStartScroll = scrollY;
                    Capture = true;
                }
                ClampScroll();
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                Entry r = RowAt(e.Location);
                if (r != null && r != selected) { selected = r; Invalidate(); }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingThumb)
            {
                draggingThumb = false;
                Capture = false;
                UpdateHover(e.Location);
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                int h = HeaderAt(e.Location);
                if (h == 2) GoBack();
                else if (h == 3) Rescan();
                else if (h == 0 && sortBySize) { sortBySize = false; SortRows(); Invalidate(); }
                else if (h == 1 && !sortBySize) { sortBySize = true; SortRows(); scrollY = 0; Invalidate(); }
            }
            else if (e.Button == MouseButtons.Right)
            {
                Entry r = RowAt(e.Location);
                if (r != null) ShowRowMenu(r, PointToScreen(e.Location));
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            Entry r = RowAt(e.Location);
            if (r != null) OpenEntry(r);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scrollY -= e.Delta * 3 * S(RowH) / 120;
            ClampScroll();
            UpdateHover(e.Location);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        void ClampScroll()
        {
            if (fontRow == null) return;
            LayoutInfo l = GetLayout();
            int max = Math.Max(0, rows.Count * l.RowH - (l.ListBottom - l.ListTop));
            scrollY = Math.Max(0, Math.Min(scrollY, max));
        }

        void EnsureVisible(Entry e)
        {
            int i = rows.IndexOf(e);
            if (i < 0 || fontRow == null) return;
            LayoutInfo l = GetLayout();
            int top = i * l.RowH, listH = l.ListBottom - l.ListTop;
            if (top < scrollY) scrollY = top;
            else if (top + l.RowH > scrollY + listH) scrollY = top + l.RowH - listH;
            ClampScroll();
        }

        void ShowRowMenu(Entry e, Point screen)
        {
            const int Analyse = 1, Open = 2, Reveal = 3, OpenFile = 4, Copy = 5;
            IntPtr m = Native.CreatePopupMenu();
            try
            {
                if (e.IsDir && !e.IsLink)
                {
                    Native.AppendMenuW(m, Native.MF_STRING, (UIntPtr)Analyse, "Analyser ce dossier");
                    Native.AppendMenuW(m, Native.MF_STRING, (UIntPtr)Open, "Ouvrir dans l'Explorateur");
                    Native.SetMenuDefaultItem(m, Analyse, 0);
                }
                else if (!e.IsDir)
                {
                    Native.AppendMenuW(m, Native.MF_STRING, (UIntPtr)OpenFile, "Ouvrir");
                }
                Native.AppendMenuW(m, Native.MF_STRING, (UIntPtr)Reveal, "Afficher dans l'Explorateur");
                if (e.IsDir && e.IsLink) Native.SetMenuDefaultItem(m, Reveal, 0);
                if (!e.IsDir) Native.SetMenuDefaultItem(m, Reveal, 0);
                Native.AppendMenuW(m, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Native.AppendMenuW(m, Native.MF_STRING, (UIntPtr)Copy, "Copier le chemin");

                int cmd = Native.TrackPopupMenuEx(m, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, screen.X, screen.Y, Handle, IntPtr.Zero);
                switch (cmd)
                {
                    case Analyse: Navigate(e.Path, true); break;
                    case Open: OpenInExplorer(e); break;
                    case Reveal: ShowInExplorer(e); break;
                    case OpenFile: try { Process.Start(e.Path); } catch { } break;
                    case Copy: try { Clipboard.SetText(e.Path); } catch { } break;
                }
            }
            finally { Native.DestroyMenu(m); }
        }

        // ------------------------------------------------------------------ clavier

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.PageUp: MoveSelection(-PageRows()); return true;
                case Keys.PageDown: MoveSelection(PageRows()); return true;
                case Keys.Home: MoveSelection(-rows.Count); return true;
                case Keys.End: MoveSelection(rows.Count); return true;
                case Keys.Enter: OpenEntry(selected); return true;
                case Keys.Back:
                case Keys.Alt | Keys.Left: GoBack(); return true;
                case Keys.Alt | Keys.Up: GoParent(); return true;
                case Keys.F5: Rescan(); return true;
                case Keys.Control | Keys.C:
                    if (selected != null) try { Clipboard.SetText(selected.Path); } catch { }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        int PageRows()
        {
            LayoutInfo l = GetLayout();
            return Math.Max(1, (l.ListBottom - l.ListTop) / l.RowH - 1);
        }

        void MoveSelection(int delta)
        {
            if (rows.Count == 0) return;
            int i = selected == null ? (delta > 0 ? -1 : rows.Count) : rows.IndexOf(selected);
            i = Math.Max(0, Math.Min(rows.Count - 1, i + delta));
            selected = rows[i];
            EnsureVisible(selected);
            Invalidate();
        }

        // Taper les premières lettres d'un nom pour le sélectionner, comme dans l'Explorateur.
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar) || rows.Count == 0) return;
            if ((DateTime.Now - typeAheadTime).TotalMilliseconds > 900) typeAhead = "";
            typeAheadTime = DateTime.Now;
            typeAhead += e.KeyChar;
            int start = selected == null ? 0 : rows.IndexOf(selected);
            if (typeAhead.Length == 1) start++;
            for (int k = 0; k < rows.Count; k++)
            {
                Entry r = rows[(Math.Max(0, start) + k) % rows.Count];
                if (r.Name.StartsWith(typeAhead, StringComparison.CurrentCultureIgnoreCase))
                {
                    selected = r;
                    EnsureVisible(r);
                    Invalidate();
                    break;
                }
            }
            e.Handled = true;
        }

        // ------------------------------------------------------------------ capture (tests)

        void TakeSnapshot()
        {
            try
            {
                // Test du découpage : PESEE_SNAP_SCROLL = décalage de défilement en pixels.
                int forcedScroll;
                if (int.TryParse(Environment.GetEnvironmentVariable("PESEE_SNAP_SCROLL"), out forcedScroll))
                {
                    scrollY = forcedScroll;
                    ClampScroll();
                }
                using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) Render(g);
                    bmp.Save(snapshotPath, ImageFormat.Png);
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("dossier      : " + currentPath);
                sb.AppendLine("liste        : " + listMs + " ms");
                sb.AppendLine("analyse      : " + (scan != null ? scan.ElapsedMs : 0) + " ms");
                sb.AppendLine("threads      : " + (scan != null ? scan.ThreadCount : 0));
                sb.AppendLine("dossiers     : " + (scan != null ? scan.DirsScanned : 0));
                sb.AppendLine("depuis lancement : " + (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms");
                sb.AppendLine("echelle      : " + scale);
                long files = 0;
                foreach (Entry e in allEntries) files += Interlocked.Read(ref e.Files);
                sb.AppendLine("fichiers     : " + files);
                sb.AppendLine("total        : " + TotalSize() + " octets (" + FormatSize(TotalSize()) + ")");
                foreach (Entry e in rows)
                    sb.AppendLine("  " + e.Name.PadRight(40) + " " + Interlocked.Read(ref e.Size).ToString().PadLeft(16) + "  " + FormatSize(e.Size));
                File.WriteAllText(Path.ChangeExtension(snapshotPath, ".txt"), sb.ToString(), Encoding.UTF8);
            }
            finally { Close(); }
        }
    }
}
