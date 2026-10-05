using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    public class HexView : Control
    {
        public Project P;
        public string File;
        public int Cursor2;          // posicion del cursor (offset)
        public int TopRow;
        public event EventHandler CursorMoved;
        VScrollBar sb;
        int nibble;                  // 0 = alto, 1 = bajo (al teclear)
        const int Cols = 16;
        int rowH = 18, charW = 9;

        public HexView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Theme.Mono;
            BackColor = Theme.Bg;
            sb = new VScrollBar { Dock = DockStyle.Right };
            sb.Scroll += delegate { TopRow = sb.Value; Invalidate(); };
            Controls.Add(sb);
        }

        byte[] Data { get { return (P == null || File == null) ? null : P.Get(File); } }
        byte[] Orig { get { return (P == null || File == null) ? null : P.GetOriginal(File); } }

        public void Reload()
        {
            byte[] d = Data; if (d == null) return;
            sb.Minimum = 0; sb.Maximum = Math.Max(0, (d.Length + Cols - 1) / Cols - 1 + VisibleRows() - 1); sb.LargeChange = VisibleRows();
            Cursor2 = Math.Min(Cursor2, d.Length - 1);
            GoTo(Cursor2);
        }

        int VisibleRows() { return Math.Max(1, (Height - rowH - 4) / rowH); }

        public void GoTo(int off)
        {
            byte[] d = Data; if (d == null) return;
            if (off < 0) off = 0; if (off >= d.Length) off = d.Length - 1;
            Cursor2 = off; nibble = 0;
            int row = off / Cols;
            if (row < TopRow || row >= TopRow + VisibleRows()) TopRow = Math.Max(0, row - VisibleRows() / 3);
            sb.Value = Math.Min(sb.Maximum, TopRow);
            Invalidate();
            if (CursorMoved != null) CursorMoved(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (sb != null && Data != null) { sb.LargeChange = VisibleRows(); } Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Theme.Bg);
            byte[] d = Data; if (d == null) return;
            byte[] o = Orig;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int x0 = 8, hexX = x0 + 10 * charW, ascX = hexX + Cols * 3 * charW + 12;
            using (SolidBrush dim = new SolidBrush(Theme.Dim), txt = new SolidBrush(Theme.Text), mod = new SolidBrush(Theme.GoodText), sel = new SolidBrush(Theme.AccentDark), hdr = new SolidBrush(Theme.Panel))
            {
                g.FillRectangle(hdr, 0, 0, Width, rowH);
                g.DrawString("Offset", Font, dim, x0, 0);
                for (int c = 0; c < Cols; c++) g.DrawString(c.ToString("X2"), Font, dim, hexX + c * 3 * charW, 0);
                g.DrawString("Texto", Font, dim, ascX, 0);
                int rows = VisibleRows() + 1;
                for (int r = 0; r < rows; r++)
                {
                    int rowIdx = TopRow + r; int off = rowIdx * Cols;
                    if (off >= d.Length) break;
                    int y = rowH + 2 + r * rowH;
                    g.DrawString(off.ToString("X8"), Font, dim, x0, y);
                    for (int c = 0; c < Cols && off + c < d.Length; c++)
                    {
                        int i = off + c;
                        bool isCur = i == Cursor2;
                        if (isCur) { g.FillRectangle(sel, hexX + c * 3 * charW - 2, y, charW * 2 + 4, rowH); g.FillRectangle(sel, ascX + c * (charW - 1) - 1, y, charW - 1, rowH); }
                        bool m = d[i] != o[i];
                        g.DrawString(d[i].ToString("X2"), Font, m ? mod : txt, hexX + c * 3 * charW, y);
                        char ch = d[i] >= 32 && d[i] < 127 ? (char)d[i] : '·';
                        g.DrawString(ch.ToString(), Font, m ? mod : (d[i] >= 32 && d[i] < 127 ? txt : dim), ascX + c * (charW - 1), y);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            byte[] d = Data; if (d == null) return;
            int hexX = 8 + 10 * charW;
            int r = (e.Y - rowH - 2) / rowH; if (e.Y < rowH) return;
            int c = (e.X - hexX) / (3 * charW);
            if (c < 0 || c >= Cols) return;
            GoTo((TopRow + r) * Cols + c);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = e.Delta > 0 ? -3 : 3;
            TopRow = Math.Max(0, Math.Min(sb.Maximum, TopRow + step));
            sb.Value = Math.Min(sb.Maximum, TopRow); Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: GoTo(Cursor2 - 1); e.Handled = true; return;
                case Keys.Right: GoTo(Cursor2 + 1); e.Handled = true; return;
                case Keys.Up: GoTo(Cursor2 - Cols); e.Handled = true; return;
                case Keys.Down: GoTo(Cursor2 + Cols); e.Handled = true; return;
                case Keys.PageUp: GoTo(Cursor2 - Cols * VisibleRows()); e.Handled = true; return;
                case Keys.PageDown: GoTo(Cursor2 + Cols * VisibleRows()); e.Handled = true; return;
                case Keys.Home: GoTo(Cursor2 - Cursor2 % Cols); e.Handled = true; return;
                case Keys.End: GoTo(Cursor2 - Cursor2 % Cols + Cols - 1); e.Handled = true; return;
            }
            int v = -1;
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9 && !e.Shift) v = e.KeyCode - Keys.D0;
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) v = e.KeyCode - Keys.NumPad0;
            else if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.F && !e.Control) v = 10 + e.KeyCode - Keys.A;
            if (v >= 0)
            {
                byte[] d = Data; byte cur = d[Cursor2];
                byte nb = nibble == 0 ? (byte)((v << 4) | (cur & 0xF)) : (byte)((cur & 0xF0) | v);
                P.Write(File, Cursor2, new byte[] { nb });
                if (nibble == 0) { nibble = 1; Invalidate(); if (CursorMoved != null) CursorMoved(this, EventArgs.Empty); }
                else GoTo(Cursor2 + 1);
                e.Handled = true;
            }
        }
    }

    public class HexPage : Page
    {
        HexView hv;
        ComboBox fileBox;
        TextBox goBox, findBox;
        CheckBox textMode;
        Label inspector;
        int lastFind = -1;

        public override string Title { get { return "Editor hexadecimal"; } }

        public HexPage()
        {
            Panel bar = MakeBar(48);
            fileBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 210 };
            goBox = new TextBox { Left = 260, Top = 10, Width = 90 };
            Label lg = Theme.MakeLabel("Ir a", true); lg.Left = 228; lg.Top = 14;
            Button bg = Theme.MakeButton("Ir", 36, false); bg.Left = 356; bg.Top = 8;
            findBox = new TextBox { Left = 410, Top = 10, Width = 200 };
            textMode = new CheckBox { Left = 618, Top = 12, Width = 110, Text = "Buscar texto" };
            Button bf = Theme.MakeButton("Buscar siguiente", 130, false); bf.Left = 732; bf.Top = 8;
            bar.Controls.AddRange(new Control[] { fileBox, lg, goBox, bg, findBox, textMode, bf });
            hv = new HexView { Dock = DockStyle.Fill };
            inspector = new Label { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Panel, ForeColor = Theme.Text, Padding = new Padding(12, 8, 12, 4), Font = Theme.Mono };
            Controls.Add(hv); Controls.Add(inspector); Controls.Add(bar);
            Theme.Apply(this);
            bar.BackColor = Theme.Panel; inspector.BackColor = Theme.Panel; inspector.Font = Theme.Mono; hv.Font = Theme.Mono; hv.BackColor = Theme.Bg;
            fileBox.SelectedIndexChanged += delegate { hv.File = (string)fileBox.SelectedItem; hv.Cursor2 = 0; hv.TopRow = 0; hv.Reload(); lastFind = -1; };
            bg.Click += delegate { Go(); }; goBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Go(); e.SuppressKeyPress = true; } };
            bf.Click += delegate { Find(); }; findBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Find(); e.SuppressKeyPress = true; } };
            hv.CursorMoved += delegate { Inspect(); };
        }

        protected override void OnProject()
        {
            hv.P = P;
            fileBox.Items.Clear();
            foreach (IsoEntry e in P.Disc.ListFiles())
                if (!e.IsDir && !e.Path.EndsWith(".STR") && e.Size <= 40u * 1024 * 1024) fileBox.Items.Add(e.Path);
            int i = fileBox.Items.IndexOf("SLUS_006.14"); fileBox.SelectedIndex = i >= 0 ? i : 0;
        }

        public void ShowAt(string file, int off)
        {
            int i = fileBox.Items.IndexOf(file); if (i >= 0) fileBox.SelectedIndex = i;
            hv.GoTo(off);
        }

        void Go()
        {
            long v;
            if (long.TryParse(goBox.Text.Trim().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out v)) hv.GoTo((int)v);
        }

        void Find()
        {
            if (hv.File == null || findBox.Text.Length == 0) return;
            byte[] pat;
            try
            {
                if (textMode.Checked) pat = P.Codec.Encode(findBox.Text);
                else
                {
                    string[] parts = findBox.Text.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    pat = new byte[parts.Length];
                    for (int i = 0; i < parts.Length; i++) pat[i] = Convert.ToByte(parts[i], 16);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Búsqueda"); return; }
            byte[] d = P.Get(hv.File);
            int start = Math.Max(hv.Cursor2 + 1, 0);
            for (int pass = 0; pass < 2; pass++)
            {
                int from = pass == 0 ? start : 0, to = pass == 0 ? d.Length : Math.Min(d.Length, start + pat.Length);
                for (int i = from; i + pat.Length <= to + (pass == 0 ? 0 : 0) && i + pat.Length <= d.Length; i++)
                {
                    bool ok = true;
                    for (int k = 0; k < pat.Length; k++) if (d[i + k] != pat[k]) { ok = false; break; }
                    if (ok) { hv.GoTo(i); hv.Focus(); return; }
                }
            }
            MessageBox.Show("No encontrado.", "Búsqueda");
        }

        void Inspect()
        {
            if (hv.File == null) return;
            byte[] d = P.Get(hv.File); int o = hv.Cursor2; if (o < 0 || o >= d.Length) return;
            StringBuilder sb = new StringBuilder();
            sb.Append("0x" + o.ToString("X") + "   u8=" + d[o]);
            if (o + 1 < d.Length) sb.Append("  u16=" + BitConverter.ToUInt16(d, o));
            if (o + 3 < d.Length) sb.Append("  u32=0x" + BitConverter.ToUInt32(d, o).ToString("X8"));
            long ram = RamMap.ToRam(hv.File, o);
            if (ram != 0) sb.Append("   RAM 0x" + ram.ToString("X8"));
            sb.Append("\r\nTexto: ");
            int n = Math.Min(24, d.Length - o);
            sb.Append(P.Codec.DecodeRaw(d, o, n).Replace("\n", "⏎").Replace("\0", "·"));
            inspector.Text = sb.ToString();
        }
    }

    public class FilesPage : Page
    {
        ListView list;
        public override string Title { get { return "Archivos del disco"; } }
        public event EventHandler<PathEventArgs> OpenInHex;

        public FilesPage()
        {
            Panel bar = MakeBar(48);
            Button bx = Theme.MakeButton("Extraer…", 100, false); bx.Left = 10; bx.Top = 8;
            Button bxa = Theme.MakeButton("Extraer todo…", 120, false); bxa.Left = 116; bxa.Top = 8;
            Button br = Theme.MakeButton("Reemplazar archivo…", 160, false); br.Left = 242; br.Top = 8;
            Button bv = Theme.MakeButton("Revertir", 90, false); bv.Left = 408; bv.Top = 8;
            Button bh = Theme.MakeButton("Abrir en hex", 110, true); bh.Left = 504; bh.Top = 8;
            Label l = Theme.MakeLabel("Reemplazar exige un archivo de tamaño ≤ al original.", true); l.Left = 630; l.Top = 14;
            bar.Controls.AddRange(new Control[] { bx, bxa, br, bv, bh, l });
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.UI };
            list.Columns.Add("Ruta", 300); list.Columns.Add("LBA", 90); list.Columns.Add("Tamaño", 110); list.Columns.Add("Sectores", 80); list.Columns.Add("Estado", 100);
            Controls.Add(list); Controls.Add(bar);
            Theme.Apply(this); bar.BackColor = Theme.Panel;
            list.BackColor = Theme.Bg;
            bx.Click += delegate { Extract(); }; bxa.Click += delegate { ExtractAll(); };
            br.Click += delegate { Replace(); }; bv.Click += delegate { Revert(); };
            bh.Click += delegate { if (Selected() != null && OpenInHex != null) OpenInHex(this, new PathEventArgs(Selected().Path)); };
            list.DoubleClick += delegate { if (Selected() != null && OpenInHex != null) OpenInHex(this, new PathEventArgs(Selected().Path)); };
        }

        protected override void OnProject() { P.Changed += delegate { UpdateStates(); }; Fill(); }
        public override void OnShow() { UpdateStates(); }

        IsoEntry Selected() { return list.SelectedItems.Count == 0 ? null : (IsoEntry)list.SelectedItems[0].Tag; }

        void Fill()
        {
            list.Items.Clear();
            foreach (IsoEntry e in P.Disc.ListFiles())
            {
                if (e.IsDir) continue;
                ListViewItem it = new ListViewItem(e.Path);
                it.SubItems.Add(e.Lba.ToString()); it.SubItems.Add(e.Size.ToString("N0")); it.SubItems.Add(((e.Size + 2047) / 2048).ToString()); it.SubItems.Add("");
                it.Tag = e; list.Items.Add(it);
            }
            UpdateStates();
        }

        void UpdateStates()
        {
            if (P == null) return;
            foreach (ListViewItem it in list.Items)
            {
                bool d = P.IsDirty(((IsoEntry)it.Tag).Path);
                it.SubItems[4].Text = d ? "modificado" : "";
                it.ForeColor = d ? Theme.GoodText : Theme.Text;
            }
        }

        void Extract()
        {
            IsoEntry e = Selected(); if (e == null) return;
            SaveFileDialog d = new SaveFileDialog { FileName = System.IO.Path.GetFileName(e.Path) };
            if (d.ShowDialog() != DialogResult.OK) return;
            System.IO.File.WriteAllBytes(d.FileName, P.Get(e.Path));
        }

        void ExtractAll()
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            if (d.ShowDialog() != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            foreach (IsoEntry e in P.Disc.ListFiles())
            {
                if (e.IsDir) continue;
                string p = System.IO.Path.Combine(d.SelectedPath, e.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                System.IO.File.WriteAllBytes(p, e.Path.EndsWith(".STR") ? P.Disc.ReadFile(e) : P.Get(e.Path));
                Application.DoEvents();
            }
            Cursor = Cursors.Default;
        }

        void Replace()
        {
            IsoEntry e = Selected(); if (e == null) return;
            if (e.Path.EndsWith(".STR")) { MessageBox.Show("Los vídeos STR no se editan aquí."); return; }
            OpenFileDialog d = new OpenFileDialog();
            if (d.ShowDialog() != DialogResult.OK) return;
            try { P.ReplaceFile(e.Path, System.IO.File.ReadAllBytes(d.FileName)); UpdateStates(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo reemplazar"); }
        }

        void Revert()
        {
            IsoEntry e = Selected(); if (e == null) return;
            if (MessageBox.Show("¿Descartar todos los cambios de " + e.Path + "?\r\n(Recarga las pestañas para ver el efecto.)", "Revertir", MessageBoxButtons.YesNo) == DialogResult.Yes) P.RevertFile(e.Path);
        }
    }

    public class PathEventArgs : EventArgs
    {
        public string Path;
        public PathEventArgs(string p) { Path = p; }
    }
}
