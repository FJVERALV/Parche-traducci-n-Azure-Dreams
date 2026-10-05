using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    public abstract class Page : UserControl
    {
        protected Project P;
        public Page() { Dock = DockStyle.Fill; BackColor = Theme.Bg; DoubleBuffered = true; }
        public void Attach(Project p) { P = p; OnProject(); }
        protected virtual void OnProject() { }
        public virtual void OnShow() { }
        public virtual string Title { get { return ""; } }
        public static Panel MakeBar(int h)
        {
            Panel p = new Panel { Dock = DockStyle.Top, Height = h, BackColor = Theme.Panel, Padding = new Padding(10, 6, 10, 6) };
            return p;
        }
    }

    public class TextPage : Page
    {
        public static readonly string[] Files = { "SLUS_006.14", "MAIN/MAIN.BIN", "TOWN/TOWN.BIN", "DUNGEON/DUNGEON.BIN" };
        List<TextEntry> all = new List<TextEntry>();
        List<TextEntry> view = new List<TextEntry>();

        ComboBox fileFilter, statusFilter;
        TextBox search;
        NumericUpDown minChars;
        CheckBox showJp;
        DataGridView grid;
        RichTextBox origBox, transBox;
        bool allowEnter;
        Label legendLbl;
        MeterBar meter, progress;
        Label info, ctxPrev, ctxNext, helpLbl, previewLbl;
        FlowLayoutPanel charPanel;
        int cur = -1;
        bool busy;

        public override string Title { get { return "Traducción de texto"; } }
        public List<TextEntry> Entries { get { return all; } }

        public TextPage()
        {
            Panel bar = MakeBar(48);
            fileFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 190 };
            statusFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 208, Top = 10, Width = 130 };
            statusFilter.Items.AddRange(new object[] { "Todas", "Sin traducir", "Traducidas", "Con errores", "Con avisos" });
            statusFilter.SelectedIndex = 0;
            search = new TextBox { Left = 346, Top = 10, Width = 196 };
            Label lm = Theme.MakeLabel("Mín.", true); lm.Left = 556; lm.Top = 14;
            minChars = new NumericUpDown { Left = 592, Top = 10, Width = 50, Minimum = 1, Maximum = 200, Value = 4 };
            showJp = new CheckBox { Left = 654, Top = 12, Width = 120, Text = "Ver japonés" };
            showJp.CheckedChanged += delegate { Refilter(); };
            progress = new MeterBar { Left = 840, Top = 13, Width = 220, Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Button rep = Theme.MakeButton("Reemplazar…", 100, false); rep.Left = 990; rep.Top = 9; rep.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            rep.Click += delegate { FindReplace(); };
            bar.Controls.AddRange(new Control[] { fileFilter, statusFilter, search, lm, minChars, showJp, progress, rep });
            bar.Resize += delegate { progress.Left = bar.Width - 350; rep.Left = bar.Width - 110; };

            SplitContainer sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Theme.Border, SplitterWidth = 3, FixedPanel = FixedPanel.Panel2 };
            sc.Panel1.BackColor = Theme.Bg; sc.Panel2.BackColor = Theme.Panel;
            sc.SizeChanged += delegate { try { sc.SplitterDistance = Math.Max(300, sc.Width - 470); } catch (Exception) { } };

            grid = new DataGridView { Dock = DockStyle.Fill, VirtualMode = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoGenerateColumns = false };
            AddCol("id", "#", 52); AddCol("file", "Archivo", 90); AddCol("off", "Offset", 68); AddCol("max", "Máx car.", 74); AddCol("used", "Usa", 42);
            AddCol("orig", "Original", 260);
            DataGridViewTextBoxColumn tc = new DataGridViewTextBoxColumn { Name = "tr", HeaderText = "Traducción", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true };
            grid.Columns.Add(tc);
            Theme.StyleGrid(grid);
            grid.CellValueNeeded += GridValueNeeded;
            grid.CellFormatting += GridFormat;
            grid.SelectionChanged += delegate { if (!busy) SelectFromGrid(); };
            sc.Panel1.Controls.Add(grid);

            // ----- panel de edicion
            Panel ed = sc.Panel2;
            ed.Padding = new Padding(14);
            info = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Dim, Text = "Selecciona una cadena" };
            Label l1 = Theme.MakeLabel("ORIGINAL", true); l1.Dock = DockStyle.Top; l1.Height = 22; l1.AutoSize = false;
            origBox = new RichTextBox { Dock = DockStyle.Top, Height = 92, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Font = Theme.Mono, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false };
            legendLbl = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Dim, Padding = new Padding(0, 4, 0, 0) };
            Label l2 = Theme.MakeLabel("TRADUCCIÓN   (texto normal: se convierte solo a ancho completo)", true); l2.Dock = DockStyle.Top; l2.Height = 26; l2.AutoSize = false; l2.Padding = new Padding(0, 8, 0, 0);
            transBox = new RichTextBox { Dock = DockStyle.Top, Height = 130, ScrollBars = RichTextBoxScrollBars.Vertical, Font = Theme.Mono, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false, AcceptsTab = false };
            meter = new MeterBar { Dock = DockStyle.Top, Height = 24 };
            FlowLayoutPanel btns = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(0, 10, 0, 0) };
            Button bPrev = Theme.MakeButton("◀ Anterior", 100, false), bNext = Theme.MakeButton("Siguiente ▶", 100, false);
            Button bCopy = Theme.MakeButton("Copiar original", 112, false), bNextU = Theme.MakeButton("Sig. sin traducir ⏭", 152, true);
            Button bClear = Theme.MakeButton("Borrar", 70, false);
            bPrev.Click += delegate { MoveSel(-1); }; bNext.Click += delegate { MoveSel(1); };
            bCopy.Click += delegate { if (Sel != null) transBox.Text = Sel.Original; transBox.Focus(); };
            bNextU.Click += delegate { NextUntranslated(); };
            bClear.Click += delegate { transBox.Text = ""; };
            btns.Controls.AddRange(new Control[] { bPrev, bNext, bCopy, bClear, bNextU });
            charPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, AutoScroll = false };
            ctxPrev = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Dim, Padding = new Padding(0, 6, 0, 0) };
            ctxNext = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Dim, Padding = new Padding(0, 6, 0, 0) };
            helpLbl = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Dim, Text = "Ctrl+↓ / Ctrl+↑: siguiente / anterior\r\nCtrl+Enter: siguiente sin traducir\r\nVacío = no se modifica el juego.\r\nRojo = excede el espacio o carácter no válido.\r\nNaranja = aviso: línea de más de 31 caracteres o\r\nventana de más de 3 líneas (se vería cortado).\r\nLas opciones [..] no deben pasar de 31 caracteres." };
            ed.Controls.Add(helpLbl);
            previewLbl = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Theme.GoodText, Padding = new Padding(0, 6, 0, 0) };
            ed.Controls.Add(ctxNext); ed.Controls.Add(ctxPrev); ed.Controls.Add(charPanel); ed.Controls.Add(btns); ed.Controls.Add(previewLbl); ed.Controls.Add(meter);
            ed.Controls.Add(transBox); ed.Controls.Add(l2); ed.Controls.Add(legendLbl); ed.Controls.Add(origBox); ed.Controls.Add(l1); ed.Controls.Add(info);

            transBox.TextChanged += delegate { if (!busy) OnTransChanged(); };
            transBox.KeyDown += TransKey;
            grid.KeyDown += TransKey;

            Controls.Add(sc); Controls.Add(bar);
            fileFilter.SelectedIndexChanged += delegate { Refilter(); };
            statusFilter.SelectedIndexChanged += delegate { Refilter(); };
            search.TextChanged += delegate { Refilter(); };
            minChars.ValueChanged += delegate { Refilter(); };
            Theme.Apply(this);
            grid.BackgroundColor = Theme.Bg;
            Theme.StyleGrid(grid);
            origBox.BackColor = Theme.Panel; transBox.BackColor = Theme.Panel2;
            ed.BackColor = Theme.Panel; helpLbl.BackColor = Color.Transparent; info.BackColor = Color.Transparent;
            foreach (Control c in ed.Controls) if (c is FlowLayoutPanel || c is Label) c.BackColor = Color.Transparent;
            bar.BackColor = Theme.Panel;
        }

        void AddCol(string name, string header, int w)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = w, ReadOnly = true });
        }

        TextEntry Sel { get { return (cur >= 0 && cur < view.Count) ? view[cur] : null; } }

        // ------------------------------------------------------------ carga
        protected override void OnProject()
        {
            all.Clear(); view.Clear(); cur = -1;
            fileFilter.Items.Clear(); fileFilter.Items.Add("(todos los archivos)");
            Cursor = Cursors.WaitCursor;
            foreach (string n in Files)
            {
                if (P.Find(n) == null) continue;
                fileFilter.Items.Add(n);
                all.AddRange(P.Codec.Scan(P.GetOriginal(n), n, 3));
                Application.DoEvents();
            }
            Cursor = Cursors.Default;
            int id = 0; foreach (TextEntry t in all) t.Id = ++id;
            BuildPointerIndex();
            RebuildCharButtons();
            fileFilter.SelectedIndex = 0;
            Refilter();
        }

        // punteros a cadenas: palabras alineadas de SLUS y MAIN cuyo valor es la direccion RAM de un texto
        Dictionary<uint, int> ptrCount = new Dictionary<uint, int>();

        void BuildPointerIndex()
        {
            ptrCount.Clear();
            foreach (string g in new string[] { RamMap.Slus, RamMap.Main })
            {
                if (P.Find(g) == null) continue;
                byte[] gb = P.GetOriginal(g);
                for (int i = 0; i + 4 <= gb.Length; i += 4)
                {
                    uint w = BitConverter.ToUInt32(gb, i);
                    if (w >= 0x8000B800u && w < 0x80081800u) { int c; ptrCount.TryGetValue(w, out c); ptrCount[w] = c + 1; }
                }
            }
        }

        string PointerInfo(TextEntry t)
        {
            long ram = RamMap.ToRam(t.File, (int)t.Offset);
            if (ram == 0) return "  ·  script (sin punteros directos)";
            int c; ptrCount.TryGetValue((uint)ram, out c);
            return c > 0 ? "  ·  " + c + " puntero(s) → RAM 0x" + ram.ToString("X8") : "  ·  sin puntero directo";
        }

        public void RebuildCharButtons()
        {
            charPanel.Controls.Clear();
            foreach (char c in P.Codec.ExtraChars())
            {
                Button b = Theme.MakeButton(c.ToString(), 32, false); b.Height = 30; b.Font = new Font("Segoe UI", 11f);
                char cc = c;
                b.Click += delegate { transBox.Focus(); transBox.SelectedText = cc.ToString(); };
                charPanel.Controls.Add(b);
            }
            // insertar codigos de control en la traduccion
            string[][] tags = { new string[] { "{HEROE}", "Nombre del héroe" }, new string[] { "{PAG}", "Salto de página" }, new string[] { "{VENT}", "Abrir ventana" } };
            foreach (string[] tg in tags)
            {
                Button tb = Theme.MakeButton(tg[0], 74, false); tb.Height = 28; tb.Font = new Font("Segoe UI", 8.5f);
                string tag = tg[0];
                tb.Click += delegate { transBox.Focus(); transBox.SelectedText = tag; };
                new ToolTip().SetToolTip(tb, "Insertar " + tg[1]);
                charPanel.Controls.Add(tb);
            }
            Label hint = Theme.MakeLabel(P.Codec.HasExtra ? "" : "Tildes y ñ: se convierten solas", true);
            charPanel.Controls.Add(hint);
        }

        void Refilter()
        {
            string f = fileFilter.SelectedIndex > 0 ? (string)fileFilter.SelectedItem : null;
            string q = search.Text.Trim().ToLowerInvariant();
            int st = statusFilter.SelectedIndex, min = (int)minChars.Value;
            TextEntry keep = Sel;
            view = new List<TextEntry>();
            int tr = 0, tot = 0;
            foreach (TextEntry t in all)
            {
                if (!t.IsJapanese) tot++;
                if (t.Translation.Length > 0 && t.UsedBytes >= 0 && t.UsedBytes <= t.Length) tr++;
                if (f != null && t.File != f) continue;
                if (t.MaxChars < min) continue;
                if (t.IsJapanese && !showJp.Checked) continue;
                bool has = t.Translation.Length > 0;
                bool err = has && (t.UsedBytes < 0 || t.UsedBytes > t.Length);
                if (st == 1 && has) continue;
                if (st == 2 && !(has && !err)) continue;
                if (st == 3 && !err) continue;
                if (st == 4 && !(has && !err && t.Warn.Length > 0)) continue;
                if (q.Length > 0 && t.Original.ToLowerInvariant().IndexOf(q) < 0 && t.Translation.ToLowerInvariant().IndexOf(q) < 0) continue;
                view.Add(t);
            }
            busy = true;
            grid.RowCount = 0; grid.RowCount = view.Count;
            busy = false;
            progress.Set(tr, Math.Max(1, tot), false, tr + " traducidas / " + tot + " (inglés)");
            int idx = keep != null ? view.IndexOf(keep) : -1;
            if (idx < 0 && view.Count > 0) idx = 0;
            if (idx >= 0) GoTo(idx); else { cur = -1; ClearEditor(); }
        }

        void ClearEditor() { busy = true; origBox.Text = ""; transBox.Text = ""; info.Text = "Sin resultados"; meter.Set(0, 1, false, ""); ctxPrev.Text = ctxNext.Text = ""; busy = false; }

        // ------------------------------------------------------------ grid
        void GridValueNeeded(object s, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            TextEntry t = view[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = t.Id; break;
                case 1: e.Value = t.File.Substring(t.File.LastIndexOf('/') + 1); break;
                case 2: e.Value = t.Offset.ToString("X6"); break;
                case 3: e.Value = t.MaxChars; break;
                case 4: e.Value = t.UsedBytes == 0 ? "" : (t.UsedBytes < 0 ? "!" : t.UsedChars.ToString()); break;
                case 5: e.Value = t.Original.Replace("\n", " ⏎ ") + (t.EndsDialog ? "  ⏹" : ""); break;
                case 6: e.Value = t.Translation.Replace("\n", " ⏎ "); break;
            }
        }

        void GridFormat(object s, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            TextEntry t = view[e.RowIndex];
            if (t.Translation.Length == 0) return;
            bool bad = t.Bad, warn = !bad && t.Warn.Length > 0;
            if (e.ColumnIndex == 4 || e.ColumnIndex == 6)
            {
                e.CellStyle.ForeColor = bad ? Theme.BadText : warn ? Theme.Warn : Theme.GoodText;
                if (bad) e.CellStyle.BackColor = Color.FromArgb(58, 30, 33);
            }
        }

        void SelectFromGrid()
        {
            if (grid.CurrentRow == null) return;
            int i = grid.CurrentRow.Index;
            if (i == cur) return;
            cur = i; ShowCurrent();
        }

        void GoTo(int i)
        {
            if (i < 0 || i >= view.Count) return;
            cur = i;
            busy = true;
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[i].Cells[0];
            grid.Rows[i].Selected = true;
            busy = false;
            ShowCurrent();
        }

        void MoveSel(int d) { int i = cur + d; if (i >= 0 && i < view.Count) { GoTo(i); transBox.Focus(); } }

        void NextUntranslated()
        {
            for (int i = cur + 1; i < view.Count; i++)
                if (view[i].Translation.Length == 0) { GoTo(i); transBox.Focus(); return; }
            MessageBox.Show("No quedan cadenas sin traducir en esta vista.", "Fin");
        }

        void TransKey(object s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Down) { MoveSel(1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Up) { MoveSel(-1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.Enter) { NextUntranslated(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (s == transBox && e.KeyCode == Keys.Enter && !allowEnter) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        // ------------------------------------------------------------ edicion
        void ShowCurrent()
        {
            TextEntry t = Sel; if (t == null) return;
            busy = true;
            allowEnter = t.Original.IndexOf('\n') >= 0 || t.Original.Contains(SjisCodec.TagPage) || t.Original.Contains(SjisCodec.TagWin);
            origBox.Text = t.Original + (t.EndsDialog ? "\n⏹ FIN DEL DIÁLOGO" : "");
            transBox.Text = t.Translation;
            info.Text = t.File + "  @0x" + t.Offset.ToString("X6") + PointerInfo(t) + "\r\n" + t.Length + " bytes = " + t.MaxChars + " caracteres de texto (+" + t.CtlBytes + " bytes de códigos)";
            legendLbl.Text = Legend(t);
            Colorize(origBox, Theme.Text);
            busy = false;
            UpdateMeter(t);
            int gi = all.IndexOf(t);
            ctxPrev.Text = gi > 0 ? "↑ " + Short(all[gi - 1].Original) : "";
            ctxNext.Text = gi + 1 < all.Count ? "↓ " + Short(all[gi + 1].Original) : "";
        }

        static string Short(string s) { s = s.Replace("\n", " ⏎ "); return s.Length > 70 ? s.Substring(0, 70) + "…" : s; }

        /// <summary>Saltos de linea solo donde el original los tiene; en el resto, se pasan a espacio.</summary>
        public static string NormalizeTrans(TextEntry t, string raw)
        {
            string tx = raw.Replace("\r\n", "\n").Replace("\r", "\n");
            bool keep = t.Original.IndexOf('\n') >= 0 || t.Original.Contains(SjisCodec.TagPage) || t.Original.Contains(SjisCodec.TagWin);
            return keep ? tx : tx.Replace("\n", " ");
        }

        /// <summary>Leyenda de los codigos que contiene este texto.</summary>
        static string Legend(TextEntry t)
        {
            List<string> l = new List<string>();
            if (t.Original.Contains(SjisCodec.TagHero)) l.Add("{HEROE} = nombre del héroe (Koh por defecto, o el que elijas)");
            if (t.Original.Contains(SjisCodec.TagHero2)) l.Add("{HEROE2} = nombre del héroe (variante)");
            if (t.Original.Contains(SjisCodec.TagPage)) l.Add("{PAG} = salto de página (espera botón)");
            if (t.Original.Contains(SjisCodec.TagWin)) l.Add("{VENT} = abre ventana de mensaje");
            if (t.Original.IndexOf('\n') >= 0) l.Add("⏎ = salto de línea");
            if (t.EndsDialog) l.Add("⏹ = fin del diálogo");
            return l.Count == 0 ? "Sin códigos de control." : string.Join("   ·   ", l.ToArray());
        }

        static Font tagFont;
        static System.Text.RegularExpressions.Regex tagRx2 = new System.Text.RegularExpressions.Regex(@"\{[^}\r\n]{1,8}\}");

        /// <summary>Pinta las etiquetas {..} de color y el resto con el color base.</summary>
        void Colorize(RichTextBox rb, Color baseColor)
        {
            if (tagFont == null) tagFont = new Font(rb.Font, FontStyle.Bold);
            int s = rb.SelectionStart, l = rb.SelectionLength;
            bool ob = busy; busy = true;
            rb.SelectAll(); rb.SelectionColor = baseColor; rb.SelectionFont = rb.Font;
            string text = rb.Text;
            foreach (System.Text.RegularExpressions.Match m in tagRx2.Matches(text))
            {
                string v = m.Value.ToUpperInvariant();
                Color c = v.StartsWith("{HEROE") ? Color.FromArgb(255, 176, 66) :
                          v == "{PAG}" ? Color.FromArgb(90, 190, 255) :
                          v == "{VENT}" ? Color.FromArgb(190, 140, 255) : Color.FromArgb(160, 164, 172);
                rb.Select(m.Index, m.Length); rb.SelectionColor = c; rb.SelectionFont = tagFont;
            }
            int fin = text.IndexOf("⏹ FIN");
            if (fin >= 0) { rb.Select(fin, text.Length - fin); rb.SelectionColor = Color.FromArgb(255, 120, 120); rb.SelectionFont = tagFont; }
            rb.Select(Math.Min(s, rb.TextLength), l);
            busy = ob;
        }

        void UpdatePreview(TextEntry t)
        {
            List<string> lines = new List<string>();
            if (t.Translation.Length > 0)
            {
                string simp = P.Codec.Simplify(t.Translation);
                if (simp != t.Translation) lines.Add("En el juego se verá (sin tildes): " + simp.Replace("\n", " ⏎ "));
            }
            previewLbl.ForeColor = Theme.GoodText;
            previewLbl.Text = string.Join("\r\n", lines.ToArray());
            string cmp = t.Translation.Length > 0 ? P.Codec.CompareControl(t.Original, t.Translation) : "";
            if (cmp.Length > 0)
            {
                previewLbl.ForeColor = Theme.Warn;
                previewLbl.Text = (previewLbl.Text.Length > 0 ? previewLbl.Text + "\r\n" : "") + "⚠ Códigos distintos al original: " + cmp;
            }
            if (t.Translation.Length > 0 && t.Warn.Length > 0)
            {
                previewLbl.ForeColor = Theme.Warn;
                previewLbl.Text = (previewLbl.Text.Length > 0 ? previewLbl.Text + "\r\n" : "") + "⚠ No cabe en la ventana del juego: " + t.Warn + ". Añade saltos de línea (⏎) o acorta.";
            }
        }

        void UpdateMeter(TextEntry t)
        {
            UpdatePreview(t);
            int max = t.MaxChars;
            if (t.Translation.Length == 0) { meter.Set(0, t.Length, false, "0 / " + max + " caracteres (sin traducir)"); Colorize(transBox, Theme.Text); return; }
            if (t.UsedBytes == -2) { meter.Set(t.Length, t.Length, true, "Un mensaje interno (entre códigos {01}) no cabe en su hueco: acórtalo"); Colorize(transBox, Theme.BadText); return; }
            if (t.UsedBytes < 0) { meter.Set(t.Length, t.Length, true, "Carácter no válido en el juego"); Colorize(transBox, Theme.BadText); return; }
            int left = t.Length - t.UsedBytes;           // bytes libres (negativo = te pasas)
            int chars = t.UsedChars;
            string state = left < 0 ? "TE PASAS de " + ((-left + 1) / 2) + " caracteres" : left == 0 ? "justo" : "sobran " + (left / 2) + " caracteres";
            meter.Set(t.UsedBytes, t.Length, left < 0, chars + " / " + max + " caracteres · " + state + "   (" + t.UsedBytes + " / " + t.Length + " bytes)");
            Colorize(transBox, left < 0 ? Theme.BadText : Theme.GoodText);
        }

        public string ApplyEntry(TextEntry t)
        {
            if (t.Translation.Length == 0)
            {
                t.UsedBytes = 0;
                P.Write(t.File, (int)t.Offset, t.OrigBytes);
                return null;
            }
            byte[] enc;
            t.Warn = "";
            try
            {
                enc = P.Codec.Encode(t.Translation);
                // si no cabe, se prueba sin los signos de apertura (¿ ¡), como al crear el parche
                if (enc.Length > t.Length) { byte[] e2 = P.Codec.Encode(SelfTest.NoOpening(t.Translation)); if (e2.Length <= t.Length) enc = e2; }
            }
            catch (Exception ex) { t.UsedBytes = -1; P.Write(t.File, (int)t.Offset, t.OrigBytes); return ex.Message; }
            t.UsedBytes = enc.Length;
            t.UsedCtl = P.Codec.CountControl(t.Translation);
            if (enc.Length > t.Length) { P.Write(t.File, (int)t.Offset, t.OrigBytes); return "Excede " + (enc.Length - t.Length) + " bytes"; }
            byte[] o = SjisCodec.PadMessages(t.OrigBytes, enc);   // cada mensaje interno (tras 0x01) se queda en su sitio
            if (o == null)
            {
                try { byte[] e2 = P.Codec.Encode(SelfTest.NoOpening(t.Translation)); o = SjisCodec.PadMessages(t.OrigBytes, e2); if (o != null) enc = e2; }
                catch (Exception) { }
            }
            if (o == null) { t.UsedBytes = -2; P.Write(t.File, (int)t.Offset, t.OrigBytes); return "Un mensaje interno (separado por {01}) no cabe en su hueco"; }
            t.Warn = SjisCodec.LayoutWarnings(enc, t.OrigBytes);
            P.Write(t.File, (int)t.Offset, o);
            return null;
        }

        void OnTransChanged()
        {
            TextEntry t = Sel; if (t == null) return;
            t.Translation = NormalizeTrans(t, transBox.Text);
            ApplyEntry(t);
            UpdateMeter(t);
            grid.InvalidateRow(cur);
            int tr = 0, tot = 0;
            foreach (TextEntry x in all) { if (!x.IsJapanese) tot++; if (x.Translation.Length > 0 && x.UsedBytes >= 0 && x.UsedBytes <= x.Length) tr++; }
            progress.Set(tr, Math.Max(1, tot), false, tr + " traducidas / " + tot + " (inglés)");
        }

        // ------------------------------------------------------------ proyecto
        public void SaveTsv(string path, bool onlyTranslated) { ProjectIO.Save(path, all, onlyTranslated); }

        /// <summary>Exporta a CSV (Excel). Por defecto solo el texto en ingles; includeJp anade el japones.</summary>
        public int SaveCsv(string path, bool onlyTranslated, bool includeJp)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "id", "archivo", "offset", "max_caracteres", "max_bytes", "original", "traduccion" });
            foreach (TextEntry t in all)
            {
                if (t.IsJapanese && !includeJp) continue;
                if (onlyTranslated && t.Translation.Length == 0) continue;
                rows.Add(new string[] { t.Id.ToString(), t.File, t.Offset.ToString("X"), t.MaxChars.ToString(), t.Length.ToString(), t.Original, t.Translation });
            }
            Csv.Write(path, rows, Csv.ExcelEs);
            return rows.Count - 1;
        }

        /// <summary>Importa traducciones desde CSV: empareja por archivo+offset. Devuelve (aplicadas, con_error).</summary>
        public string LoadCsv(string path)
        {
            List<string[]> rows = Csv.Read(path);
            if (rows.Count < 2) return "El CSV está vacío.";
            string[] h = rows[0];
            int cf = Csv.Col(h, "archivo"), co = Csv.Col(h, "offset"), ct = Csv.Col(h, "traduccion");
            if (cf < 0 || co < 0 || ct < 0) return "Faltan columnas: se necesitan 'archivo', 'offset' y 'traduccion'.";
            Dictionary<string, TextEntry> map = new Dictionary<string, TextEntry>();
            foreach (TextEntry e in all) map[e.File + "|" + e.Offset.ToString("X")] = e;
            int ok = 0, bad = 0, miss = 0;
            List<string> errs = new List<string>();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                if (r.Length <= Math.Max(cf, Math.Max(co, ct))) continue;
                TextEntry t;
                long off;
                if (!long.TryParse(r[co].Trim(), System.Globalization.NumberStyles.HexNumber, null, out off) ||
                    !map.TryGetValue(r[cf].Trim() + "|" + off.ToString("X"), out t)) { miss++; continue; }
                t.Translation = NormalizeTrans(t, r[ct]);
                string err = ApplyEntry(t);
                if (err != null && t.Translation.Length > 0) { bad++; if (errs.Count < 8) errs.Add("#" + t.Id + " " + err); } else ok++;
            }
            Refilter();
            string msg = ok + " traducciones importadas";
            if (bad > 0) msg += ", " + bad + " con error (no se aplican al juego):\r\n" + string.Join("\r\n", errs.ToArray());
            if (miss > 0) msg += "\r\n" + miss + " filas no coinciden con ninguna cadena.";
            return msg;
        }

        /// <summary>Uso interno (capturas de prueba): filtra por texto y escribe una traduccion en la cadena seleccionada.</summary>
        public void DebugShow(string search_, string trans)
        {
            if (!string.IsNullOrEmpty(search_)) search.Text = search_;
            if (!string.IsNullOrEmpty(trans) && Sel != null) transBox.Text = trans;
        }

        public int LastUnmatched;

        public int LoadTsv(string path)
        {
            int n = ProjectIO.Load(path, all, out LastUnmatched);
            foreach (TextEntry t in all) if (t.Translation.Length > 0) ApplyEntry(t);
            Refilter();
            return n;
        }

        public void RefreshAll()
        {
            foreach (TextEntry t in all) if (t.Translation.Length > 0) ApplyEntry(t);
            Refilter();
        }

        void FindReplace()
        {
            using (Form f = new Form { Text = "Reemplazar en traducciones", Width = 420, Height = 200, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                TextBox a = new TextBox { Left = 100, Top = 16, Width = 280 }, b = new TextBox { Left = 100, Top = 52, Width = 280 };
                Label la = new Label { Left = 14, Top = 20, Text = "Buscar", AutoSize = true }, lb = new Label { Left = 14, Top = 56, Text = "Reemplazar", AutoSize = true };
                Button ok = new Button { Text = "Reemplazar todo", Left = 240, Top = 100, Width = 140, DialogResult = DialogResult.OK };
                f.Controls.AddRange(new Control[] { a, b, la, lb, ok });
                Theme.Apply(f); Theme.StyleButton(ok, true);
                f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK || a.Text.Length == 0) return;
                int n = 0;
                foreach (TextEntry t in all)
                    if (t.Translation.Length > 0 && t.Translation.IndexOf(a.Text, StringComparison.Ordinal) >= 0) { t.Translation = t.Translation.Replace(a.Text, b.Text); ApplyEntry(t); n++; }
                Refilter();
                MessageBox.Show(n + " traducciones modificadas.");
            }
        }
    }
}
