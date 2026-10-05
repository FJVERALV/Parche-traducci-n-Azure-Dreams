using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    public class GfxParams
    {
        public string File;
        public int Offset;
        public int Bpp = 4;
        public int W = 128, H = 128;
        public int TileW = 128, TileH = 128;
        public bool Lsb = true;   // nibble/bit bajo primero (PSX)
        public int Bytes { get { return (int)((long)W * H * Bpp / 8); } }
        public int RowBytes { get { return Math.Max(1, W * Bpp / 8); } }

        long BitOf(int x, int y)
        {
            int tpr = Math.Max(1, W / TileW);
            int t = (y / TileH) * tpr + (x / TileW);
            long pix = (long)t * TileW * TileH + (y % TileH) * TileW + (x % TileW);
            return pix * Bpp;
        }

        public int Get(byte[] d, int x, int y)
        {
            long bit = BitOf(x, y);
            int bi = Offset + (int)(bit >> 3);
            if (bi < 0 || bi + (Bpp == 16 ? 1 : 0) >= d.Length) return 0;
            if (Bpp == 16) return d[bi] | (d[bi + 1] << 8);
            if (Bpp == 8) return d[bi];
            int pos = (int)(bit & 7);
            int shift = Lsb ? pos : 8 - Bpp - pos;
            return (d[bi] >> shift) & ((1 << Bpp) - 1);
        }

        public bool Set(byte[] d, int x, int y, int v)
        {
            long bit = BitOf(x, y);
            int bi = Offset + (int)(bit >> 3);
            if (bi < 0 || bi + (Bpp == 16 ? 1 : 0) >= d.Length) return false;
            if (Bpp == 16) { d[bi] = (byte)v; d[bi + 1] = (byte)(v >> 8); return true; }
            if (Bpp == 8) { d[bi] = (byte)v; return true; }
            int pos = (int)(bit & 7);
            int shift = Lsb ? pos : 8 - Bpp - pos;
            int mask = ((1 << Bpp) - 1) << shift;
            d[bi] = (byte)((d[bi] & ~mask) | ((v << shift) & mask));
            return true;
        }
    }

    public static class Psx
    {
        public static Color FromPsx(int v)
        {
            if (v == 0) return Color.FromArgb(0, 0, 0, 0);
            return Color.FromArgb(255, (v & 31) * 255 / 31, ((v >> 5) & 31) * 255 / 31, ((v >> 10) & 31) * 255 / 31);
        }
        public static int ToPsx(Color c)
        {
            if (c.A < 128) return 0;
            int v = (c.R * 31 / 255) | ((c.G * 31 / 255) << 5) | ((c.B * 31 / 255) << 10);
            return v == 0 ? 0x8000 : v;
        }
    }

    /// <summary>Lienzo con zoom que dibuja un buffer ARGB y avisa de clics/arrastres.</summary>
    public class PixelView : Control
    {
        public int[] Pix = new int[1];
        public int ImgW = 1, ImgH = 1, Zoom = 4;
        public bool Grid;
        public int TileW, TileH;
        public event Action<int, int, MouseButtons, bool> PixelMouse; // x,y,button,isDown
        Bitmap bmp;
        int lastX = -1, lastY = -1;
        public PixelView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Cross;
        }
        public void SetImage(int[] pix, int w, int h)
        {
            Pix = pix; ImgW = w; ImgH = h;
            if (bmp != null) bmp.Dispose();
            bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pix, 0, bd.Scan0, w * h);
            bmp.UnlockBits(bd);
            Size = new Size(w * Zoom, h * Zoom);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int cs = 8;
            using (SolidBrush a = new SolidBrush(Color.FromArgb(58, 58, 62)), b = new SolidBrush(Color.FromArgb(46, 46, 50)))
            {
                g.FillRectangle(a, ClientRectangle);
                for (int y = 0; y < Height; y += cs)
                    for (int x = (y / cs) % 2 * cs; x < Width; x += cs * 2) g.FillRectangle(b, x, y, cs, cs);
            }
            if (bmp == null) return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, new Rectangle(0, 0, ImgW * Zoom, ImgH * Zoom), 0, 0, ImgW, ImgH, GraphicsUnit.Pixel);
            if (Grid && Zoom >= 6)
                using (Pen p = new Pen(Color.FromArgb(50, 255, 255, 255)))
                {
                    for (int x = 0; x <= ImgW; x++) g.DrawLine(p, x * Zoom, 0, x * Zoom, ImgH * Zoom);
                    for (int y = 0; y <= ImgH; y++) g.DrawLine(p, 0, y * Zoom, ImgW * Zoom, y * Zoom);
                }
            if (TileW > 0 && TileH > 0 && (TileW < ImgW || TileH < ImgH))
                using (Pen p = new Pen(Color.FromArgb(120, 79, 140, 255)))
                {
                    for (int x = 0; x <= ImgW; x += TileW) g.DrawLine(p, x * Zoom, 0, x * Zoom, ImgH * Zoom);
                    for (int y = 0; y <= ImgH; y += TileH) g.DrawLine(p, 0, y * Zoom, ImgW * Zoom, y * Zoom);
                }
        }
        void Fire(MouseEventArgs e, bool down)
        {
            int x = e.X / Zoom, y = e.Y / Zoom;
            if (x < 0 || y < 0 || x >= ImgW || y >= ImgH) return;
            if (!down && x == lastX && y == lastY) return;
            lastX = x; lastY = y;
            if (PixelMouse != null) PixelMouse(x, y, e.Button, down);
        }
        protected override void OnMouseDown(MouseEventArgs e) { lastX = lastY = -1; Fire(e, true); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button != MouseButtons.None) Fire(e, false); base.OnMouseMove(e); }
    }

    public class PaletteBar : Control
    {
        public Color[] Colors = new Color[16];
        public int Selected;
        public int Cell = 22;
        public event Action<int> Picked;
        public event Action<int> EditRequested;
        public PaletteBar() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
        int PerRow { get { return Math.Max(1, Width / Cell); } }
        public void SetColors(Color[] c)
        {
            Colors = c;
            if (Selected >= c.Length) Selected = 0;
            Height = Math.Max(Cell + 4, ((c.Length + PerRow - 1) / PerRow) * Cell + 4);
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Colors != null) SetColors(Colors); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Theme.Panel);
            int pr = PerRow;
            for (int i = 0; i < Colors.Length; i++)
            {
                Rectangle r = new Rectangle((i % pr) * Cell + 2, (i / pr) * Cell + 2, Cell - 2, Cell - 2);
                using (SolidBrush ck = new SolidBrush(Color.FromArgb(70, 70, 74))) g.FillRectangle(ck, r);
                using (SolidBrush b = new SolidBrush(Colors[i])) g.FillRectangle(b, r);
                if (i == Selected) using (Pen p = new Pen(Color.White, 2)) g.DrawRectangle(p, r);
            }
        }
        int Hit(MouseEventArgs e)
        {
            int i = (e.Y / Cell) * PerRow + (e.X / Cell);
            return (i >= 0 && i < Colors.Length) ? i : -1;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = Hit(e); if (i < 0) return;
            Selected = i; Invalidate();
            if (Picked != null) Picked(i);
            base.OnMouseDown(e);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = Hit(e); if (i >= 0 && EditRequested != null) EditRequested(i);
            base.OnMouseDoubleClick(e);
        }
    }

    public class GfxPage : Page
    {
        GfxParams gp = new GfxParams();
        ComboBox fileBox, preset, bppBox, palMode;
        TextBox offBox, palOffBox;
        NumericUpDown wBox, hBox, twBox, thBox, zoomBox;
        CheckBox lsbBox, gridBox, compBox;
        RadioButton toolPen, toolFill, toolPick;
        PixelView view;
        PaletteBar palBar;
        Panel scroller, palHost;
        Label status;
        TrackBar pos;
        ListView catalogList;
        ImageList catalogThumbs;
        Label catalogStatus;
        Color[] pal = new Color[16];
        class UndoRec { public string File; public int Off; public bool Comp; public byte[] Data; }
        List<UndoRec> undo = new List<UndoRec>();
        bool compDirty;
        Button saveBtn;
        bool busy;
        int fg = 1;
        byte[] decodedCache; int decodedCacheOffset = -1; string decodedCacheFile; int decodedConsumed;
        bool catalogTried;
        static readonly string[] CatalogFiles = { "SLUS_006.14", "MAIN/MAIN.BIN", "TOWN/TOWN.BIN", "DUNGEON/DUNGEON.BIN" };
        string CatalogCachePath { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gfx_catalog.csv"); } }

        public override string Title { get { return "Imágenes y fuentes"; } }

        public GfxPage()
        {
            Panel side = new Panel { Dock = DockStyle.Left, Width = 292, BackColor = Theme.Panel, Padding = new Padding(12), AutoScroll = true };
            int y = 8;
            Func<string, Control, int, Control> row = delegate(string label, Control c, int h)
            {
                Label l = Theme.MakeLabel(label, true); l.Left = 0; l.Top = y + 4;
                c.Left = 100; c.Top = y; c.Width = 165;
                side.Controls.Add(l); side.Controls.Add(c);
                y += h; return c;
            };
            fileBox = (ComboBox)row("Archivo", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
            offBox = (TextBox)row("Offset (hex)", new TextBox { Text = "0" }, 30);
            preset = (ComboBox)row("Formato", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
            bppBox = (ComboBox)row("Bits/píxel", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
            wBox = (NumericUpDown)row("Ancho", new NumericUpDown { Minimum = 1, Maximum = 1024, Value = 128 }, 30);
            hBox = (NumericUpDown)row("Alto", new NumericUpDown { Minimum = 1, Maximum = 1024, Value = 128 }, 30);
            twBox = (NumericUpDown)row("Tile ancho", new NumericUpDown { Minimum = 1, Maximum = 1024, Value = 128 }, 30);
            thBox = (NumericUpDown)row("Tile alto", new NumericUpDown { Minimum = 1, Maximum = 1024, Value = 128 }, 30);
            lsbBox = (CheckBox)row("", new CheckBox { Text = "Bits bajos primero (PSX)", Checked = true }, 28);
            compBox = (CheckBox)row("", new CheckBox { Text = "Datos comprimidos (LZ)" }, 28);
            palMode = (ComboBox)row("Paleta", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 30);
            palOffBox = (TextBox)row("Paleta offset", new TextBox { Text = "0" }, 30);
            zoomBox = (NumericUpDown)row("Zoom", new NumericUpDown { Minimum = 1, Maximum = 32, Value = 4 }, 30);
            gridBox = (CheckBox)row("", new CheckBox { Text = "Cuadrícula", Checked = true }, 28);
            y += 4;
            Label lt = Theme.MakeLabel("HERRAMIENTA", true); lt.Left = 0; lt.Top = y; side.Controls.Add(lt); y += 22;
            toolPen = new RadioButton { Text = "Lápiz", Left = 0, Top = y, Width = 70, Checked = true };
            toolFill = new RadioButton { Text = "Relleno", Left = 74, Top = y, Width = 76 };
            toolPick = new RadioButton { Text = "Cuentagotas", Left = 150, Top = y, Width = 110 };
            side.Controls.AddRange(new Control[] { toolPen, toolFill, toolPick }); y += 34;
            Button bExp = Theme.MakeButton("Exportar PNG", 124, false); bExp.Left = 0; bExp.Top = y;
            Button bImp = Theme.MakeButton("Importar PNG", 124, false); bImp.Left = 132; bImp.Top = y;
            side.Controls.AddRange(new Control[] { bExp, bImp }); y += 36;
            Button bUndo = Theme.MakeButton("Deshacer (Ctrl+Z)", 256, false); bUndo.Left = 0; bUndo.Top = y; side.Controls.Add(bUndo); y += 36;
            saveBtn = Theme.MakeButton("Guardar en el juego", 256, true); saveBtn.Left = 0; saveBtn.Top = y; saveBtn.Enabled = false; side.Controls.Add(saveBtn); y += 40;
            saveBtn.Click += delegate { SaveCompressed(); };
            Label help = Theme.MakeLabel("Rueda del ratón: mueve una fila.\r\nMayús+rueda: una página.\r\nClic izq = color de paleta, der = borrar (índice 0).\r\nDoble clic en la paleta: editar color\r\n(si la paleta viene de un offset).\r\n\r\nEl listado de arriba muestra todas\r\nlas imágenes encontradas en los\r\nficheros del juego. Clic en una\r\npara verla y editarla aquí abajo.\r\n\r\nImágenes comprimidas: se edita la\r\nimagen y «Guardar en el juego» la\r\nrecomprime (debe caber en su hueco).\r\nSin comprimir: se guarda al pintar.\r\nLuego: Archivo > Crear BIN traducido.", true);
            help.Left = 0; help.Top = y; help.MaximumSize = new Size(260, 0); side.Controls.Add(help);

            Panel top = MakeBar(44);
            Button pp = Theme.MakeButton("⏮ Página", 84, false); pp.Left = 10; pp.Top = 7;
            Button pr = Theme.MakeButton("◀ Fila", 70, false); pr.Left = 100; pr.Top = 7;
            Button nr = Theme.MakeButton("Fila ▶", 70, false); nr.Left = 176; nr.Top = 7;
            Button np = Theme.MakeButton("Página ⏭", 84, false); np.Left = 252; np.Top = 7;
            pos = new TrackBar { Left = 350, Top = 6, Width = 260, TickStyle = TickStyle.None, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Button bScan = Theme.MakeButton("Reescanear catálogo", 150, false); bScan.Left = 620; bScan.Top = 7;
            status = Theme.MakeLabel("", true); status.Left = 780; status.Top = 14; status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.AddRange(new Control[] { pp, pr, nr, np, pos, bScan, status });
            top.Resize += delegate { pos.Width = Math.Max(100, top.Width - 350 - 480); bScan.Left = top.Width - 480; status.Left = top.Width - 300; };
            pp.Click += delegate { Nudge(-gp.Bytes); }; np.Click += delegate { Nudge(gp.Bytes); };
            pr.Click += delegate { Nudge(-gp.RowBytes); }; nr.Click += delegate { Nudge(gp.RowBytes); };
            bScan.Click += delegate { ScanAllFiles(); };

            catalogList = new ListView { Dock = DockStyle.Top, Height = 300, View = View.LargeIcon, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, MultiSelect = false, HideSelection = false };
            catalogThumbs = new ImageList { ImageSize = new Size(80, 80), ColorDepth = ColorDepth.Depth32Bit };
            catalogList.LargeImageList = catalogThumbs;
            catalogStatus = Theme.MakeLabel("Escaneando catálogo…", true); catalogStatus.Dock = DockStyle.Top; catalogStatus.Height = 22; catalogStatus.Padding = new Padding(8, 4, 0, 0);

            palHost = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Theme.Panel, AutoScroll = true, Padding = new Padding(6) };
            palBar = new PaletteBar { Dock = DockStyle.Top, Height = 60 };
            palHost.Controls.Add(palBar);
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(24, 25, 28) };
            view = new PixelView { Left = 0, Top = 0 };
            scroller.Controls.Add(view);
            Controls.Add(scroller); Controls.Add(palHost); Controls.Add(catalogList); Controls.Add(catalogStatus); Controls.Add(top); Controls.Add(side);

            preset.Items.AddRange(new object[] { "Personalizado", "Sprite 4bpp 16x16", "Hoja 4bpp 128x128 (tiles 8x8)", "Hoja 8bpp 256x128", "Hoja 16bpp 128x128", "Fuente 1bpp 16x16 (16 col.)", "Fuente 2bpp 16x16", "Fuente 4bpp 16x16 (16 col.)", "Fuente 1bpp 8x8 (32 col.)" });
            preset.SelectedIndex = 0;
            bppBox.Items.AddRange(new object[] { "1", "2", "4", "8", "16" }); bppBox.SelectedIndex = 2;
            palMode.Items.AddRange(new object[] { "Escala de grises", "Colores automáticos", "Desde offset del archivo (PSX 15 bits)" });
            palMode.SelectedIndex = 0;
            Theme.Apply(this);
            side.BackColor = Theme.Panel; palBar.BackColor = Theme.Panel; palHost.BackColor = Theme.Panel;
            foreach (Control c in side.Controls) if (c is Label || c is CheckBox || c is RadioButton) c.BackColor = Color.Transparent;
            top.BackColor = Theme.Panel;
            scroller.BackColor = Color.FromArgb(24, 25, 28);

            EventHandler reread = delegate { if (!busy) Redraw(true); };
            fileBox.SelectedIndexChanged += reread; bppBox.SelectedIndexChanged += reread;
            wBox.ValueChanged += reread; hBox.ValueChanged += reread; twBox.ValueChanged += reread; thBox.ValueChanged += reread;
            lsbBox.CheckedChanged += reread; palMode.SelectedIndexChanged += reread; gridBox.CheckedChanged += reread; zoomBox.ValueChanged += reread;
            compBox.CheckedChanged += reread;
            catalogList.SelectedIndexChanged += delegate
            {
                if (busy || catalogList.SelectedItems.Count == 0) return;
                GfxCodec.Hit h = catalogList.SelectedItems[0].Tag as GfxCodec.Hit;
                if (h == null || !ConfirmLeave()) return;
                busy = true;
                int fi = fileBox.Items.IndexOf(h.File);
                if (fi >= 0) fileBox.SelectedIndex = fi;
                offBox.Text = h.Offset.ToString("X");
                compBox.Checked = true;
                busy = false;
                Redraw(true);
            };
            offBox.Leave += reread; palOffBox.Leave += reread;
            offBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Redraw(true); e.SuppressKeyPress = true; } };
            palOffBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Redraw(true); e.SuppressKeyPress = true; } };
            preset.SelectedIndexChanged += delegate { ApplyPreset(); };
            pos.Scroll += delegate { if (!busy) { offBox.Text = ((long)pos.Value * 64).ToString("X"); Redraw(true); } };
            view.PixelMouse += OnPixel;
            palBar.Picked += delegate(int i) { fg = i; };
            palBar.EditRequested += EditPaletteColor;
            bExp.Click += delegate { ExportPng(); }; bImp.Click += delegate { ImportPng(); };
            bUndo.Click += delegate { Undo(); };
            scroller.MouseWheel += delegate(object s, MouseEventArgs e) { Nudge((e.Delta > 0 ? -1 : 1) * ((Control.ModifierKeys & Keys.Shift) != 0 ? gp.Bytes : gp.RowBytes)); };
            view.MouseEnter += delegate { scroller.Focus(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Z) Undo(); };
        }

        protected override void OnProject()
        {
            busy = true;
            fileBox.Items.Clear();
            foreach (IsoEntry e in P.Disc.ListFiles())
                if (!e.IsDir && !e.Path.EndsWith(".STR") && e.Size > 0 && e.Size <= 40u * 1024 * 1024) fileBox.Items.Add(e.Path);
            int i = fileBox.Items.IndexOf("SLUS_006.14"); fileBox.SelectedIndex = i >= 0 ? i : 0;
            busy = false;
            Redraw(true);
        }

        public override void OnShow()
        {
            if (catalogTried) return;
            catalogTried = true;
            if (!LoadCatalogCache()) ScanAllFiles();
        }

        void ApplyPreset()
        {
            int i = preset.SelectedIndex; if (i <= 0) return;
            busy = true;
            switch (i)
            {
                case 1: SetFmt(4, 16, 16, 16, 16); break;
                case 2: SetFmt(4, 128, 128, 8, 8); break;
                case 3: SetFmt(8, 256, 128, 256, 128); break;
                case 4: SetFmt(16, 128, 128, 128, 128); break;
                case 5: SetFmt(1, 256, 256, 16, 16); break;
                case 6: SetFmt(2, 256, 256, 16, 16); break;
                case 7: SetFmt(4, 256, 256, 16, 16); break;
                case 8: SetFmt(1, 256, 64, 8, 8); break;
            }
            busy = false;
            Redraw(true);
        }

        void SetFmt(int bpp, int w, int h, int tw, int th)
        {
            bppBox.SelectedItem = bpp.ToString();
            wBox.Value = w; hBox.Value = h; twBox.Value = tw; thBox.Value = th;
        }

        void Nudge(int d)
        {
            long o = ParseHex(offBox.Text) + d;
            byte[] data = CurData(); if (data == null) return;
            if (o < 0) o = 0; if (o > data.Length - 1) o = data.Length - 1;
            offBox.Text = o.ToString("X");
            Redraw(true);
        }

        static long ParseHex(string s) { long v; return long.TryParse(s.Trim().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out v) ? v : 0; }

        byte[] CurData() { return fileBox.SelectedItem == null ? null : P.Get((string)fileBox.SelectedItem); }

        void ReadParams()
        {
            gp.File = (string)fileBox.SelectedItem;
            gp.Offset = (int)ParseHex(offBox.Text);
            gp.Bpp = int.Parse((string)bppBox.SelectedItem);
            gp.W = (int)wBox.Value; gp.H = (int)hBox.Value;
            gp.TileW = Math.Min(gp.W, (int)twBox.Value); gp.TileH = Math.Min(gp.H, (int)thBox.Value);
            if (gp.W % gp.TileW != 0) gp.TileW = gp.W;
            if (gp.H % gp.TileH != 0) gp.TileH = gp.H;
            gp.Lsb = lsbBox.Checked;
        }

        void BuildPalette()
        {
            int n = gp.Bpp >= 16 ? 0 : 1 << gp.Bpp;
            pal = new Color[Math.Max(n, 1)];
            byte[] d = CurData();
            int mode = palMode.SelectedIndex;
            for (int i = 0; i < n; i++)
            {
                if (mode == 0) { int v = n == 1 ? 255 : i * 255 / (n - 1); pal[i] = Color.FromArgb(v, v, v); }
                else if (mode == 1) pal[i] = i == 0 ? Color.FromArgb(0, 0, 0, 0) : HsvColor((i * 360.0 / n * 1.0) % 360, 0.65, 0.95);
                else
                {
                    int po = (int)ParseHex(palOffBox.Text) + i * 2;
                    pal[i] = (d != null && po + 1 < d.Length) ? Psx.FromPsx(d[po] | (d[po + 1] << 8)) : Color.Magenta;
                }
            }
        }

        static Color HsvColor(double h, double s, double v)
        {
            int hi = (int)(h / 60) % 6; double f = h / 60 - Math.Floor(h / 60);
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            double r, g, b;
            switch (hi) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        /// <summary>Si "Datos comprimidos" esta marcado, descomprime desde gp.Offset (con cache)
        /// y devuelve el buffer resultante; null si falla o esta desactivado.</summary>
        byte[] DecodedData()
        {
            if (!compBox.Checked) return null;
            string f = gp.File; int off = gp.Offset;
            if (decodedCache != null && decodedCacheFile == f && decodedCacheOffset == off) return decodedCache;
            byte[] raw = CurData();
            decodedCache = null; decodedCacheFile = f; decodedCacheOffset = off; decodedConsumed = 0;
            if (raw == null) return null;
            int consumed;
            byte[] dec = GfxCodec.TryDecode(raw, off, 1 << 20, out consumed);
            decodedCache = dec; decodedConsumed = consumed;
            return dec;
        }

        void Redraw(bool full)
        {
            if (fileBox.SelectedItem == null) return;
            ReadParams();
            if (compDirty && (gp.File != decodedCacheFile || gp.Offset != decodedCacheOffset || !compBox.Checked))
            {
                if (MessageBox.Show("La imagen anterior tiene cambios sin guardar. ¿Guardarlos en el juego?", "Imagen editada", MessageBoxButtons.YesNo) == DialogResult.Yes) SaveCompressed();
                compDirty = false; saveBtn.Enabled = false; decodedCacheOffset = -1;
            }
            byte[] fileData = CurData();
            byte[] dec = DecodedData();
            byte[] d = dec ?? fileData;
            int savedOffset = gp.Offset;
            if (dec != null) gp.Offset = 0;
            if (full) BuildPalette();
            int[] px = new int[gp.W * gp.H];
            for (int y = 0; y < gp.H; y++)
                for (int x = 0; x < gp.W; x++)
                {
                    int v = gp.Get(d, x, y);
                    Color c = gp.Bpp == 16 ? Psx.FromPsx(v) : pal[v];
                    px[y * gp.W + x] = c.ToArgb();
                }
            gp.Offset = savedOffset;
            view.Zoom = (int)zoomBox.Value; view.Grid = gridBox.Checked; view.TileW = gp.TileW; view.TileH = gp.TileH;
            view.SetImage(px, gp.W, gp.H);
            if (gp.Bpp < 16) { palBar.SetColors(pal); palHost.Visible = true; } else palHost.Visible = false;
            busy = true;
            pos.Maximum = Math.Max(1, fileData.Length / 64); pos.Value = Math.Min(pos.Maximum, gp.Offset / 64);
            busy = false;
            if (compBox.Checked)
                status.Text = dec == null
                    ? "0x" + gp.Offset.ToString("X") + "  flujo LZ invalido en este offset"
                    : "0x" + gp.Offset.ToString("X") + "  comprimido=" + decodedConsumed + " bytes -> descomprimido=" + dec.Length + " bytes";
            else
                status.Text = "0x" + gp.Offset.ToString("X") + " / 0x" + fileData.Length.ToString("X") + "   (" + gp.Bytes + " bytes)";
        }

        /// <summary>Dibuja una miniatura de 80x80 a partir de los bytes ya descomprimidos,
        /// asumiendo 4bpp e intentando un ancho que de una imagen mas o menos cuadrada.
        /// Es solo orientativo (no se conoce el ancho real de cada recurso).</summary>
        static Bitmap RenderThumb(byte[] decoded)
        {
            int cw = 80, ch = 80;
            int[] buf = new int[cw * ch];
            int bg = unchecked((int)0xFF14141A);
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            if (decoded != null && decoded.Length > 0)
            {
                int pixelCount = decoded.Length * 2;
                int w = Math.Max(8, ((int)Math.Round(Math.Sqrt(pixelCount) / 8.0)) * 8);
                int rowBytes = Math.Max(1, w / 2);
                int h = Math.Max(1, decoded.Length / rowBytes);
                double scale = Math.Min((double)(cw - 4) / w, (double)(ch - 4) / h);
                if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1;
                int ox = (int)((cw - w * scale) / 2), oy = (int)((ch - h * scale) / 2);
                for (int y = 0; y < h; y++)
                {
                    int py0 = oy + (int)(y * scale), py1 = oy + (int)((y + 1) * scale);
                    if (py1 <= py0) py1 = py0 + 1;
                    for (int x = 0; x < w; x++)
                    {
                        int byteIdx = y * rowBytes + x / 2;
                        if (byteIdx >= decoded.Length) continue;
                        byte b = decoded[byteIdx];
                        int nib = (x % 2 == 0) ? (b & 0xF) : ((b >> 4) & 0xF);
                        int v = nib * 17;
                        int color = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
                        int px0 = ox + (int)(x * scale), px1 = ox + (int)((x + 1) * scale);
                        if (px1 <= px0) px1 = px0 + 1;
                        for (int yy = Math.Max(0, py0); yy < Math.Min(ch, py1); yy++)
                            for (int xx = Math.Max(0, px0); xx < Math.Min(cw, px1); xx++)
                                buf[yy * cw + xx] = color;
                    }
                }
            }
            Bitmap bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }

        /// <summary>Genera la miniatura de cada hit, releyendo cada fichero una sola vez.</summary>
        void BuildThumbs(List<GfxCodec.Hit> hits)
        {
            Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
            foreach (GfxCodec.Hit h in hits)
            {
                byte[] raw;
                if (!cache.TryGetValue(h.File, out raw)) { raw = P.Get(h.File); cache[h.File] = raw; }
                if (raw == null) continue;
                int consumed;
                byte[] dec = GfxCodec.TryDecode(raw, h.Offset, Math.Max(4096, h.OutLen + 64), out consumed);
                h.Thumb = RenderThumb(dec);
            }
        }

        void PopulateCatalogList(List<GfxCodec.Hit> hits)
        {
            busy = true;
            catalogList.BeginUpdate();
            catalogList.Items.Clear();
            catalogThumbs.Images.Clear();
            hits.Sort(delegate (GfxCodec.Hit a, GfxCodec.Hit b)
            {
                int c = string.Compare(a.File, b.File, StringComparison.Ordinal);
                return c != 0 ? c : a.Offset.CompareTo(b.Offset);
            });
            int idx = 0;
            foreach (GfxCodec.Hit h in hits)
            {
                catalogThumbs.Images.Add(h.Thumb ?? RenderThumb(null));
                string shortFile = h.File.Contains("/") ? h.File.Substring(h.File.LastIndexOf('/') + 1) : h.File;
                ListViewItem it = new ListViewItem(shortFile + "\n0x" + h.Offset.ToString("X") + "\n" + h.OutLen + " B", idx);
                it.Tag = h;
                catalogList.Items.Add(it);
                idx++;
            }
            catalogList.EndUpdate();
            catalogStatus.Text = hits.Count == 0 ? "Sin imágenes en el catálogo todavía." : hits.Count + " imágenes encontradas en " + CatalogFiles.Length + " ficheros.";
            busy = false;
        }

        void SaveCatalogCache(List<GfxCodec.Hit> hits)
        {
            try
            {
                using (StreamWriter w = new StreamWriter(CatalogCachePath, false, Encoding.UTF8))
                {
                    w.WriteLine("archivo;offset;comprimido;descomprimido;razon");
                    foreach (GfxCodec.Hit h in hits)
                        w.WriteLine(h.File + ";" + h.Offset.ToString("X") + ";" + h.Consumed + ";" + h.OutLen + ";" + h.Ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception) { /* cache es opcional */ }
        }

        bool LoadCatalogCache()
        {
            try
            {
                if (!File.Exists(CatalogCachePath)) return false;
                List<GfxCodec.Hit> hits = new List<GfxCodec.Hit>();
                string[] lines = File.ReadAllLines(CatalogCachePath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] c = lines[i].Split(';'); if (c.Length < 5) continue;
                    hits.Add(new GfxCodec.Hit
                    {
                        File = c[0],
                        Offset = Convert.ToInt32(c[1], 16),
                        Consumed = int.Parse(c[2]),
                        OutLen = int.Parse(c[3]),
                        Ratio = double.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
                if (hits.Count == 0) return false;
                catalogStatus.Text = "Cargando miniaturas del catálogo…";
                System.Threading.Thread th = new System.Threading.Thread(delegate ()
                {
                    BuildThumbs(hits);
                    BeginInvoke((Action)delegate { PopulateCatalogList(hits); });
                });
                th.IsBackground = true;
                th.Start();
                return true;
            }
            catch (Exception) { return false; }
        }

        void ScanAllFiles()
        {
            List<GfxCodec.Hit> all = new List<GfxCodec.Hit>();
            catalogStatus.Text = "Escaneando catálogo…";
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                foreach (string f in CatalogFiles)
                {
                    try
                    {
                        BeginInvoke((Action)delegate { catalogStatus.Text = "Escaneando " + f + "..."; });
                        byte[] raw = P.Get(f);
                        if (raw == null) continue;
                        List<GfxCodec.Hit> hits = GfxCodec.Scan(raw, 500, 40000, 1.8, 65536);
                        foreach (GfxCodec.Hit h in hits) h.File = f;
                        lock (all) all.AddRange(hits);
                        int count = hits.Count;
                        BeginInvoke((Action)delegate { catalogStatus.Text = f + ": " + count + " hallados"; });
                    }
                    catch (Exception ex)
                    {
                        Exception exc = ex;
                        BeginInvoke((Action)delegate { catalogStatus.Text = "Error en " + f + ": " + exc.Message; });
                    }
                }
                BeginInvoke((Action)delegate { catalogStatus.Text = "Generando miniaturas…"; });
                BuildThumbs(all);
                BeginInvoke((Action)delegate
                {
                    PopulateCatalogList(all);
                    SaveCatalogCache(all);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        // Buffer sobre el que se pinta: la imagen descomprimida (si "Datos comprimidos" esta activo) o el archivo tal cual.
        // Con imagen comprimida los cambios se quedan en memoria hasta pulsar "Guardar en el juego" (se recomprime).
        byte[] EditBuf(out bool comp)
        {
            byte[] dec = DecodedData();
            comp = dec != null;
            return dec ?? CurData();
        }

        void MarkDirty(bool comp)
        {
            if (comp) { compDirty = true; saveBtn.Enabled = true; } else P.ForceDirty(gp.File);
        }

        void PushUndo()
        {
            bool comp; byte[] d = EditBuf(out comp);
            int off = comp ? 0 : gp.Offset;
            int len = comp ? d.Length : Math.Min(gp.Bytes, d.Length - off); if (len <= 0) return;
            byte[] snap = new byte[len]; Buffer.BlockCopy(d, off, snap, 0, len);
            undo.Add(new UndoRec { File = gp.File, Off = gp.Offset, Comp = comp, Data = snap });
            if (undo.Count > 40) undo.RemoveAt(0);
        }

        void Undo()
        {
            if (undo.Count == 0) return;
            UndoRec u = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            if (u.Comp)
            {
                if (decodedCache == null || decodedCacheFile != u.File || decodedCacheOffset != u.Off) return;
                Buffer.BlockCopy(u.Data, 0, decodedCache, 0, Math.Min(u.Data.Length, decodedCache.Length));
                MarkDirty(true);
            }
            else P.Write(u.File, u.Off, u.Data);
            Redraw(false);
        }

        /// <summary>Recomprime la imagen editada y la escribe en su sitio si cabe en el hueco original.</summary>
        bool SaveCompressed()
        {
            if (!compDirty || decodedCache == null) { MessageBox.Show(compBox.Checked ? "No hay cambios que guardar." : "Esta imagen no está comprimida: los cambios ya se guardan directamente."); return true; }
            string file = decodedCacheFile; int off = decodedCacheOffset;
            byte[] enc = GfxCodec.Encode(decodedCache);
            if (enc.Length > decodedConsumed)
            {
                MessageBox.Show("La imagen editada no cabe: comprimida ocupa " + enc.Length + " bytes y el hueco original es de " + decodedConsumed +
                    " bytes (" + (enc.Length - decodedConsumed) + " de más).\r\n\r\nPrueba con menos detalle o zonas de un solo color (se comprimen mejor).", "No cabe");
                return false;
            }
            int c2; byte[] chk = GfxCodec.TryDecode(enc, 0, decodedCache.Length + 64, out c2);
            bool ok = chk != null && chk.Length == decodedCache.Length;
            for (int i = 0; ok && i < chk.Length; i++) if (chk[i] != decodedCache[i]) ok = false;
            if (!ok) { MessageBox.Show("Error interno: la imagen recomprimida no se descomprime igual. No se ha guardado."); return false; }
            byte[] keep = (byte[])decodedCache.Clone();
            P.Write(file, off, enc);
            decodedCache = keep; decodedCacheFile = file; decodedCacheOffset = off; decodedConsumed = c2;
            compDirty = false; saveBtn.Enabled = false;
            status.Text = "Guardada: " + enc.Length + " de " + decodedConsumed + " bytes";
            return true;
        }

        /// <summary>Antes de cambiar de imagen: pregunta si hay cambios sin guardar. Devuelve false si se cancela.</summary>
        bool ConfirmLeave()
        {
            if (!compDirty) return true;
            DialogResult r = MessageBox.Show("La imagen tiene cambios sin guardar. ¿Guardarlos en el juego?", "Imagen editada", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes && !SaveCompressed()) return false;
            compDirty = false; saveBtn.Enabled = false; decodedCacheOffset = -1;
            return true;
        }

        int CurrentPaintValue()
        {
            return gp.Bpp == 16 ? Psx.ToPsx(fgColor16) : fg;
        }

        Color fgColor16 = Color.White;

        void OnPixel(int x, int y, MouseButtons b, bool down)
        {
            if (toolPick.Checked && !down) return;
            if (down && !toolPick.Checked) PushUndo();
            bool comp; byte[] d = EditBuf(out comp);
            int saved = gp.Offset; if (comp) gp.Offset = 0;
            try
            {
                if (toolPick.Checked)
                {
                    int v = gp.Get(d, x, y);
                    if (gp.Bpp == 16) fgColor16 = Psx.FromPsx(v); else { fg = v; palBar.Selected = v; palBar.Invalidate(); }
                    return;
                }
                int val = b == MouseButtons.Right ? 0 : CurrentPaintValue();
                if (toolFill.Checked)
                {
                    if (!down) return;
                    Flood(d, x, y, val);
                    MarkDirty(comp);
                }
                else if (gp.Set(d, x, y, val)) MarkDirty(comp);   // escribe en el buffer vivo
            }
            finally { gp.Offset = saved; }
            Redraw(false);
        }

        void Flood(byte[] d, int sx, int sy, int val)
        {
            int target = gp.Get(d, sx, sy);
            if (target == val) return;
            Stack<int> st = new Stack<int>(); st.Push(sy * gp.W + sx);
            while (st.Count > 0)
            {
                int p = st.Pop(); int x = p % gp.W, y = p / gp.W;
                if (x < 0 || y < 0 || x >= gp.W || y >= gp.H) continue;
                if (gp.Get(d, x, y) != target) continue;
                gp.Set(d, x, y, val);
                if (x > 0) st.Push(p - 1); if (x + 1 < gp.W) st.Push(p + 1);
                if (y > 0) st.Push(p - gp.W); if (y + 1 < gp.H) st.Push(p + gp.W);
            }
        }

        void EditPaletteColor(int i)
        {
            if (palMode.SelectedIndex != 2 || gp.Bpp >= 16) { MessageBox.Show("Solo se pueden editar colores cuando la paleta viene de un offset del archivo."); return; }
            ColorDialog cd = new ColorDialog { Color = pal[i], FullOpen = true };
            if (cd.ShowDialog() != DialogResult.OK) return;
            int v = Psx.ToPsx(Color.FromArgb(255, cd.Color));
            int po = (int)ParseHex(palOffBox.Text) + i * 2;
            P.Write(gp.File, po, new byte[] { (byte)v, (byte)(v >> 8) });
            Redraw(true);
        }

        void ExportPng()
        {
            SaveFileDialog sd = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "grafico_" + gp.Offset.ToString("X") + ".png" };
            if (sd.ShowDialog() != DialogResult.OK) return;
            using (Bitmap bm = new Bitmap(gp.W, gp.H, PixelFormat.Format32bppArgb))
            {
                for (int i = 0; i < view.Pix.Length; i++) bm.SetPixel(i % gp.W, i / gp.W, Color.FromArgb(view.Pix[i]));
                bm.Save(sd.FileName, ImageFormat.Png);
            }
        }

        void ImportPng()
        {
            OpenFileDialog od = new OpenFileDialog { Filter = "Imagen (*.png;*.bmp;*.gif)|*.png;*.bmp;*.gif" };
            if (od.ShowDialog() != DialogResult.OK) return;
            PushUndo();
            bool comp; byte[] d = EditBuf(out comp);
            int saved = gp.Offset; if (comp) gp.Offset = 0;
            try
            {
                using (Bitmap bm = new Bitmap(od.FileName))
                {
                    int w = Math.Min(gp.W, bm.Width), h = Math.Min(gp.H, bm.Height);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            Color c = bm.GetPixel(x, y);
                            int v = gp.Bpp == 16 ? Psx.ToPsx(c) : Nearest(c);
                            gp.Set(d, x, y, v);
                        }
                }
            }
            finally { gp.Offset = saved; }
            MarkDirty(comp);
            Redraw(false);
        }

        int Nearest(Color c)
        {
            if (c.A < 128) return 0;
            int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                if (pal[i].A == 0 && i == 0) continue;
                long dr = c.R - pal[i].R, dg = c.G - pal[i].G, db = c.B - pal[i].B;
                long dd = dr * dr + dg * dg + db * db;
                if (dd < bd) { bd = dd; best = i; }
            }
            return best;
        }
    }
}
