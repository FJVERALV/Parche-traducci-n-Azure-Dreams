using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    /// <summary>
    /// Editor de la fuente de los dialogos y de la tabla de caracteres (ver FontPatch y DOCUMENTACION.md):
    ///   atlas 128x128 4bpp comprimido (MAIN.BIN 0x2257D8 y su copia en DUNGEON.BIN 0x4C27D8), 16x8 celdas de 8x16;
    ///   codigo SJIS -> (numeros y letras por rango | tabla de 40 simbolos en el SLUS) -> byte -> celda = byte - 0x20.
    /// Para anadir una letra: se elige un simbolo de la tabla que ningun texto use, se le asigna la letra y una
    /// celda, y se dibuja la letra en esa celda. charmap.txt guarda la asignacion (CODIGO=letra@celda).
    /// </summary>
    public class FontPage : Page
    {
        byte[] atlas;            // copia de trabajo (8192 bytes)
        bool dirty;
        int sel = 0, color = 8;
        AtlasView atlasView;
        CellView cellView;
        Panel palPanel;
        DataGridView table;
        Label info, status;
        List<KeyValuePair<ushort, byte>> entries = new List<KeyValuePair<ushort, byte>>();
        string[] letter = new string[0];
        string[] cellTxt = new string[0];

        public override string Title { get { return "Fuente"; } }

        public FontPage()
        {
            Panel bar = MakeBar(48);
            Button bAuto = Theme.MakeButton("Letras del español (auto)", 190, false); bAuto.Left = 10; bAuto.Top = 9;
            Button bExp = Theme.MakeButton("Exportar PNG", 110, false); bExp.Left = 208; bExp.Top = 9;
            Button bImp = Theme.MakeButton("Importar PNG", 110, false); bImp.Left = 326; bImp.Top = 9;
            Button bRev = Theme.MakeButton("Fuente original", 120, false); bRev.Left = 444; bRev.Top = 9;
            Button bSave = Theme.MakeButton("Guardar fuente en el juego", 200, true); bSave.Left = 572; bSave.Top = 9;
            status = Theme.MakeLabel("", true); status.Left = 784; status.Top = 15; status.AutoSize = true;
            bar.Controls.AddRange(new Control[] { bAuto, bExp, bImp, bRev, bSave, status });
            bAuto.Click += delegate { if (atlas == null) return; int c; byte[] o = FontPatch.LoadAtlas(P, true, out c); atlas = FontPatch.Build(o); Changed("Letras á é í ó ú ü ñ Ñ ¿ ¡ dibujadas en sus celdas por defecto"); };
            bExp.Click += delegate { ExportPng(); };
            bImp.Click += delegate { ImportPng(); };
            bRev.Click += delegate { if (atlas == null) return; int c; atlas = FontPatch.LoadAtlas(P, true, out c); Changed("Fuente original cargada (pulsa Guardar para aplicarla)"); };
            bSave.Click += delegate { SaveFont(); };

            Panel left = new Panel { Dock = DockStyle.Left, Width = 560, BackColor = Theme.Bg, Padding = new Padding(10), AutoScroll = true };
            atlasView = new AtlasView { Left = 10, Top = 10 };
            atlasView.CellClicked += delegate(int c) { sel = c; ShowCell(); };
            left.Controls.Add(atlasView);

            Panel mid = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = Theme.Panel, Padding = new Padding(12) };
            info = new Label { Left = 12, Top = 10, Width = 236, Height = 54, ForeColor = Theme.Text };
            cellView = new CellView { Left = 12, Top = 70 };
            cellView.Painted += delegate { dirty = true; atlasView.Invalidate(); status.Text = "Cambios sin guardar"; };
            palPanel = new Panel { Left = 12, Top = 70 + 16 * CellView.Z + 12, Width = 236, Height = 64 };
            Label help = new Label { Left = 12, Top = palPanel.Top + 70, Width = 236, Height = 200, ForeColor = Theme.Dim, Text =
                "Clic izq.: pinta · Clic der.: borra.\r\n\r\n" +
                "Letra nueva: en la tabla elige un\r\nsímbolo que ningún texto use, pon\r\nla letra y la celda (0-127), dibújala\r\n" +
                "aquí y pulsa «Guardar charmap.txt»\r\ny «Guardar fuente en el juego»." };
            mid.Controls.AddRange(new Control[] { info, cellView, palPanel, help });

            Panel right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(10) };
            Label lt = Theme.MakeLabel("TABLA DE SÍMBOLOS DEL JUEGO Y CHARMAP", true); lt.Dock = DockStyle.Top; lt.Height = 24; lt.AutoSize = false;
            table = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Código", Width = 56, ReadOnly = true });
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Símb.", Width = 44, ReadOnly = true });
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Celda", Width = 46, ReadOnly = true, ToolTipText = "Celda que usa el juego original" });
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Letra", Width = 46, ToolTipText = "Letra nueva que se escribirá con este código" });
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Celda nueva", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ToolTipText = "Celda (0-127) donde está dibujada la letra nueva" });
            table.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                int c; if (int.TryParse(Convert.ToString(table.Rows[e.RowIndex].Cells[4].Value), out c) && c >= 0 && c < 128) { sel = c; ShowCell(); }
                else { sel = entries[e.RowIndex].Value - 0x20; if (sel >= 0 && sel < 128) ShowCell(); }
            };
            Panel tbar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            Button bMap = Theme.MakeButton("Guardar charmap.txt", 170, true); bMap.Left = 0; bMap.Top = 6;
            Label tl = Theme.MakeLabel("Vacío = como el original.", true); tl.Left = 180; tl.Top = 14; tl.AutoSize = true;
            tbar.Controls.AddRange(new Control[] { bMap, tl });
            bMap.Click += delegate { SaveCharmap(); };
            right.Controls.Add(table); right.Controls.Add(tbar); right.Controls.Add(lt);

            Controls.Add(right); Controls.Add(mid); Controls.Add(left); Controls.Add(bar);
            Theme.Apply(this);
            Theme.StyleGrid(table); table.BackgroundColor = Theme.Bg;
            bar.BackColor = Theme.Panel; mid.BackColor = Theme.Panel; left.BackColor = Theme.Bg;
            foreach (Control c in mid.Controls) if (c is Label) c.BackColor = Color.Transparent;
            BuildPalette();
        }

        // ---------------------------------------------------------------- datos
        static int Px(byte[] a, int x, int y) { int b = a[y * 64 + (x >> 1)]; return (x & 1) == 0 ? (b & 15) : (b >> 4); }
        static void SetPx(byte[] a, int x, int y, int v) { int i = y * 64 + (x >> 1); a[i] = (x & 1) == 0 ? (byte)((a[i] & 0xF0) | v) : (byte)((a[i] & 0x0F) | (v << 4)); }
        static Color Gray(int v) { int g = v * 17; return Color.FromArgb(g, g, g); }

        protected override void OnProject()
        {
            int c;
            atlas = FontPatch.LoadAtlas(P, false, out c);
            if (atlas == null) { status.Text = "No se encontró la fuente en " + FontPatch.Files[0]; return; }
            // si la fuente aun no se ha tocado y hay letras en el charmap, se muestran ya dibujadas (como quedaran en el BIN)
            if (!FontPatch.IsEdited(P) && P.Codec.HasExtra) { atlas = FontPatch.Build(atlas); status.Text = "Vista con las letras del español automáticas"; }
            dirty = false;
            LoadTable();
            atlasView.Atlas = atlas; atlasView.Labels = CellLabels();
            ShowCell();
        }

        void LoadTable()
        {
            entries = FontPatch.TableEntries(P, true);
            letter = new string[entries.Count]; cellTxt = new string[entries.Count];
            Dictionary<ushort, KeyValuePair<char, int>> map = new Dictionary<ushort, KeyValuePair<char, int>>();
            foreach (KeyValuePair<ushort, KeyValuePair<char, int>> e in P.Codec.Charmap()) map[e.Key] = e.Value;
            table.Rows.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                KeyValuePair<char, int> m; bool has = map.TryGetValue(entries[i].Key, out m);
                int cell = has ? (m.Value >= 0 ? m.Value : FontPatch.DefaultCell(entries[i].Key)) : -1;
                table.Rows.Add(entries[i].Key.ToString("X4"), Sym(entries[i].Key), entries[i].Value - 0x20, has ? m.Key.ToString() : "", cell >= 0 ? cell.ToString() : "");
            }
        }

        static string Sym(ushort code)
        {
            try { return Encoding.GetEncoding(932).GetString(new byte[] { (byte)(code >> 8), (byte)code }); } catch (Exception) { return "?"; }
        }

        /// <summary>Que caracter se dibuja con cada celda (para rotular el atlas).</summary>
        string[] CellLabels()
        {
            string[] l = new string[128];
            for (int c = 0; c < 10; c++) l[16 + c] = ((char)('0' + c)).ToString();
            for (int c = 0; c < 26; c++) { l[33 + c] = ((char)('A' + c)).ToString(); l[65 + c] = ((char)('a' + c)).ToString(); }
            foreach (KeyValuePair<ushort, byte> e in FontPatch.TableEntries(P, true)) { int c = e.Value - 0x20; if (c >= 0 && c < 128 && l[c] == null) l[c] = Sym(e.Key); }
            foreach (KeyValuePair<ushort, KeyValuePair<char, int>> e in P.Codec.Charmap())
            {
                int c = e.Value.Value >= 0 ? e.Value.Value : FontPatch.DefaultCell(e.Key);
                if (c >= 0 && c < 128) l[c] = e.Value.Key.ToString();
            }
            return l;
        }

        void Changed(string msg)
        {
            dirty = true; atlasView.Atlas = atlas; atlasView.Invalidate(); ShowCell(); status.Text = msg;
        }

        void ShowCell()
        {
            if (atlas == null) return;
            atlasView.Selected = sel; atlasView.Invalidate();
            cellView.Atlas = atlas; cellView.Cell = sel; cellView.Color = color; cellView.Invalidate();
            string[] lb = atlasView.Labels;
            info.Text = "Celda " + sel + "  (fila " + (sel / 16) + ", col. " + (sel % 16) + ")\r\nByte del juego: 0x" + (sel + 0x20).ToString("X2") +
                        (lb != null && lb[sel] != null ? "   ·   dibuja: " + lb[sel] : "");
        }

        void BuildPalette()
        {
            palPanel.Controls.Clear();
            for (int i = 0; i < 16; i++)
            {
                Panel sw = new Panel { Left = (i % 8) * 29, Top = (i / 8) * 30, Width = 26, Height = 26, BackColor = Gray(i), BorderStyle = i == color ? BorderStyle.Fixed3D : BorderStyle.FixedSingle, Tag = i };
                sw.Click += delegate(object s, EventArgs e) { color = (int)((Panel)s).Tag; cellView.Color = color; BuildPalette(); };
                new ToolTip().SetToolTip(sw, "Color " + i);
                palPanel.Controls.Add(sw);
            }
        }

        // ---------------------------------------------------------------- guardar
        void SaveFont()
        {
            if (P == null || atlas == null) return;
            string e = FontPatch.SaveAtlas(P, atlas);
            if (e != null) { MessageBox.Show(e, "Fuente"); return; }
            string t = FontPatch.ApplyTable(P);
            if (t != null) { MessageBox.Show(t, "Tabla de caracteres"); return; }
            dirty = false; status.Text = "Fuente y tabla guardadas en el juego (MAIN, DUNGEON y SLUS)";
        }

        void SaveCharmap()
        {
            if (P == null) return;
            List<KeyValuePair<ushort, KeyValuePair<char, int>>> l = new List<KeyValuePair<ushort, KeyValuePair<char, int>>>();
            HashSet<char> seen = new HashSet<char>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                string ch = Convert.ToString(table.Rows[i].Cells[3].Value).Trim();
                if (ch.Length == 0) continue;
                if (ch.Length != 1) { MessageBox.Show("Fila " + entries[i].Key.ToString("X4") + ": escribe una sola letra.", "Charmap"); return; }
                if (!seen.Add(ch[0])) { MessageBox.Show("La letra '" + ch + "' está repetida.", "Charmap"); return; }
                int cell;
                if (!int.TryParse(Convert.ToString(table.Rows[i].Cells[4].Value).Trim(), out cell) || cell < 0 || cell > 127)
                { MessageBox.Show("Fila " + entries[i].Key.ToString("X4") + " ('" + ch + "'): indica la celda (0-127) donde está dibujada.", "Charmap"); return; }
                l.Add(new KeyValuePair<ushort, KeyValuePair<char, int>>(entries[i].Key, new KeyValuePair<char, int>(ch[0], cell)));
            }
            string path = P.Codec.CharmapPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt");
            P.Codec.SaveCharmap(path, l);
            string t = FontPatch.ApplyTable(P);
            atlasView.Labels = CellLabels(); atlasView.Invalidate(); ShowCell();
            status.Text = "charmap.txt guardado (" + l.Count + " letras)" + (t != null ? " · " + t : " · tabla actualizada");
            if (CharmapSaved != null) CharmapSaved(this, EventArgs.Empty);
        }

        public event EventHandler CharmapSaved;

        void ExportPng()
        {
            if (atlas == null) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "fuente.png" };
            if (d.ShowDialog() != DialogResult.OK) return;
            using (Bitmap bm = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) bm.SetPixel(x, y, Gray(Px(atlas, x, y)));
                bm.Save(d.FileName, ImageFormat.Png);
            }
            status.Text = "Exportada: 128x128, 16 grises (color n = gris n*17)";
        }

        void ImportPng()
        {
            if (atlas == null) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "PNG (*.png)|*.png" };
            if (d.ShowDialog() != DialogResult.OK) return;
            using (Bitmap bm = new Bitmap(d.FileName))
            {
                if (bm.Width != 128 || bm.Height != 128) { MessageBox.Show("La imagen debe medir 128x128 (16 x 8 celdas de 8x16)."); return; }
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                    {
                        Color c = bm.GetPixel(x, y);
                        int v = (int)Math.Round((c.R * 0.299 + c.G * 0.587 + c.B * 0.114) / 17.0);
                        SetPx(atlas, x, y, Math.Max(0, Math.Min(15, v)));
                    }
            }
            Changed("PNG importado (pulsa «Guardar fuente en el juego»)");
        }

        public bool Dirty { get { return dirty; } }

        // ---------------------------------------------------------------- controles
        class AtlasView : Control
        {
            public const int Z = 4;
            public byte[] Atlas; public string[] Labels; public int Selected;
            public event Action<int> CellClicked;
            public AtlasView() { DoubleBuffered = true; Width = 16 * 8 * Z + 1; Height = 8 * (16 * Z + 16) + 1; BackColor = Color.Black; }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.Clear(Color.FromArgb(18, 19, 22));
                if (Atlas == null) return;
                using (Font f = new Font("Segoe UI", 8f))
                using (Brush lb = new SolidBrush(Color.FromArgb(150, 160, 175)))
                    for (int c = 0; c < 128; c++)
                    {
                        int cx = (c % 16) * 8 * Z, cy = (c / 16) * (16 * Z + 16);
                        for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++)
                            {
                                int v = Px(Atlas, (c % 16) * 8 + x, (c / 16) * 16 + y);
                                if (v != 0) using (Brush b = new SolidBrush(Gray(v))) g.FillRectangle(b, cx + x * Z, cy + y * Z, Z, Z);
                            }
                        g.DrawRectangle(c == Selected ? Pens.Orange : new Pen(Color.FromArgb(45, 48, 54)), cx, cy, 8 * Z, 16 * Z);
                        string t = c + (Labels != null && Labels[c] != null ? " " + Labels[c] : "");
                        g.DrawString(t, f, lb, cx, cy + 16 * Z + 1);
                    }
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                int col = e.X / (8 * Z), row = e.Y / (16 * Z + 16);
                if (col < 16 && row < 8 && CellClicked != null) CellClicked(row * 16 + col);
            }
        }

        class CellView : Control
        {
            public const int Z = 22;
            public byte[] Atlas; public int Cell, Color;
            public event EventHandler Painted;
            public CellView() { DoubleBuffered = true; Width = 8 * Z + 1; Height = 16 * Z + 1; }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.Clear(System.Drawing.Color.FromArgb(18, 19, 22));
                if (Atlas == null) return;
                for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++)
                    {
                        int v = Px(Atlas, (Cell % 16) * 8 + x, (Cell / 16) * 16 + y);
                        using (Brush b = new SolidBrush(Gray(v))) g.FillRectangle(b, x * Z, y * Z, Z, Z);
                        g.DrawRectangle(new Pen(System.Drawing.Color.FromArgb(50, 54, 60)), x * Z, y * Z, Z, Z);
                    }
            }
            void PaintAt(MouseEventArgs e)
            {
                if (Atlas == null || e.Button == MouseButtons.None) return;
                int x = e.X / Z, y = e.Y / Z; if (x < 0 || x >= 8 || y < 0 || y >= 16) return;
                SetPx(Atlas, (Cell % 16) * 8 + x, (Cell / 16) * 16 + y, e.Button == MouseButtons.Right ? 0 : Color);
                Invalidate(); if (Painted != null) Painted(this, EventArgs.Empty);
            }
            protected override void OnMouseDown(MouseEventArgs e) { PaintAt(e); }
            protected override void OnMouseMove(MouseEventArgs e) { PaintAt(e); }
        }
    }
}
