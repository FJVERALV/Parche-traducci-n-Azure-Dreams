using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AzTool
{
    /// <summary>Editor de los mensajes de combate, objetos y estados de la torre (bloque comprimido de DUNGEON.BIN, ver BattleMsgs).</summary>
    public class BattlePage : Page
    {
        List<BattleMsgs.Msg> msgs = new List<BattleMsgs.Msg>();
        string[] trans = new string[0];
        int[] used = new int[0];          // bytes que ocupa la traduccion (-1 caracter no valido, 0 sin traducir)
        List<int> view = new List<int>();
        DataGridView grid;
        TextBox search, origBox, transBox;
        ComboBox filter;
        MeterBar meter;
        Label info;
        int cur = -1;
        bool busy;

        public override string Title { get { return "Mensajes de combate"; } }

        public BattlePage()
        {
            Panel bar = MakeBar(48);
            filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 150 };
            filter.Items.AddRange(new object[] { "Todos", "Sin traducir", "Traducidos", "No caben" });
            filter.SelectedIndex = 0;
            search = new TextBox { Left = 170, Top = 10, Width = 240 };
            Button bImp = Theme.MakeButton("Importar CSV…", 120, false); bImp.Left = 430; bImp.Top = 9;
            Button bExp = Theme.MakeButton("Exportar CSV…", 120, false); bExp.Left = 558; bExp.Top = 9;
            bar.Controls.AddRange(new Control[] { filter, search, bImp, bExp });
            bImp.Click += delegate { AskImport(); };
            bExp.Click += delegate { AskExport(); };
            filter.SelectedIndexChanged += delegate { Refilter(); };
            search.TextChanged += delegate { Refilter(); };

            SplitContainer sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Theme.Border, SplitterWidth = 3, FixedPanel = FixedPanel.Panel2 };
            sc.Panel1.BackColor = Theme.Bg; sc.Panel2.BackColor = Theme.Panel;
            sc.SizeChanged += delegate { try { sc.SplitterDistance = Math.Max(300, sc.Width - 440); } catch (Exception) { } };
            grid = new DataGridView { Dock = DockStyle.Fill, VirtualMode = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoGenerateColumns = false, ReadOnly = true };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 46 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Offset", Width = 64 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bytes", Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Original", Width = 300 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Traducción", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            Theme.StyleGrid(grid);
            grid.CellValueNeeded += GridValue;
            grid.CellFormatting += GridFormat;
            grid.SelectionChanged += delegate { if (!busy && grid.CurrentRow != null) { cur = grid.CurrentRow.Index; ShowCurrent(); } };
            sc.Panel1.Controls.Add(grid);

            Panel ed = sc.Panel2; ed.Padding = new Padding(14);
            info = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Dim, Text = "Selecciona un mensaje" };
            Label l1 = Theme.MakeLabel("ORIGINAL", true); l1.Dock = DockStyle.Top; l1.Height = 22; l1.AutoSize = false;
            origBox = new TextBox { Dock = DockStyle.Top, Height = 70, Multiline = true, ReadOnly = true, Font = Theme.Mono, ScrollBars = ScrollBars.Vertical };
            Label l2 = Theme.MakeLabel("TRADUCCIÓN", true); l2.Dock = DockStyle.Top; l2.Height = 30; l2.AutoSize = false; l2.Padding = new Padding(0, 10, 0, 0);
            transBox = new TextBox { Dock = DockStyle.Top, Height = 90, Multiline = true, AcceptsReturn = true, Font = Theme.Mono, ScrollBars = ScrollBars.Vertical };
            meter = new MeterBar { Dock = DockStyle.Top, Height = 24 };
            Label help = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Dim, Padding = new Padding(0, 12, 0, 0), Text =
                "Son los mensajes del combate y de la torre (subir de nivel,\r\n" +
                "daño, objetos, trampas, maldiciones...). Están comprimidos\r\n" +
                "con una tabla de 54 letras: las minúsculas sin tilde ocupan\r\n" +
                "1 byte; las mayúsculas G J O Q V Z, los números y las letras\r\n" +
                "con tilde ocupan 2 o más.\r\n\r\n" +
                "El juego coloca nombres (objeto, monstruo, número) ENTRE\r\n" +
                "mensajes seguidos: respeta los espacios del principio y del\r\n" +
                "final del original, p.ej. \" recibe \" + daño + \" de daño.\"\r\n\r\n" +
                "Escribe ∅ para dejar un mensaje vacío a propósito.\r\n" +
                "Cada mensaje se queda en su sitio y con su tamaño exacto\r\n" +
                "(el código del juego apunta a cada uno por su dirección)." };
            ed.Controls.Add(help); ed.Controls.Add(meter); ed.Controls.Add(transBox); ed.Controls.Add(l2); ed.Controls.Add(origBox); ed.Controls.Add(l1); ed.Controls.Add(info);
            transBox.TextChanged += delegate { if (!busy) OnEdit(); };

            Controls.Add(sc); Controls.Add(bar);
            Theme.Apply(this);
            grid.BackgroundColor = Theme.Bg; Theme.StyleGrid(grid);
            ed.BackColor = Theme.Panel; bar.BackColor = Theme.Panel;
            origBox.BackColor = Theme.Panel; transBox.BackColor = Theme.Panel2;
            foreach (Control c in ed.Controls) if (c is Label) c.BackColor = Color.Transparent;
        }

        protected override void OnProject()
        {
            msgs = P.Find(BattleMsgs.File) != null ? BattleMsgs.Parse(P.GetOriginal(BattleMsgs.File), P.Codec) : new List<BattleMsgs.Msg>();
            trans = new string[msgs.Count]; used = new int[msgs.Count];
            for (int i = 0; i < trans.Length; i++) trans[i] = "";
            // recuperar lo ya traducido en el proyecto (p.ej. del autoguardado): cada mensaje conserva su sitio y tamaño
            if (msgs.Count > 0 && P.IsDirty(BattleMsgs.File))
            {
                List<BattleMsgs.Msg> now = BattleMsgs.Parse(P.Get(BattleMsgs.File), P.Codec);
                byte[] o = P.GetOriginal(BattleMsgs.File), c = P.Get(BattleMsgs.File);
                for (int i = 0; i < msgs.Count && i < now.Count; i++)
                {
                    bool same = true;
                    for (int k = 0; k < msgs[i].Length && same; k++) if (o[msgs[i].Offset + k] != c[msgs[i].Offset + k]) same = false;
                    if (!same) { trans[i] = now[i].Text.Length == 0 && msgs[i].Text.Length > 0 ? "∅" : now[i].Text; used[i] = BattleMsgs.NeededBytes(P.Codec, o, trans[i] == "∅" ? "" : trans[i], msgs[i].Prefix); }
                }
            }
            Refilter();
        }

        static string Show(string s) { return s.Replace("\n", " ⏎ "); }

        void Refilter()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            int f = filter.SelectedIndex;
            view.Clear();
            for (int i = 0; i < msgs.Count; i++)
            {
                bool has = trans[i].Length > 0, bad = has && (used[i] < 0 || used[i] > msgs[i].Length);
                if (f == 1 && has) continue;
                if (f == 2 && !(has && !bad)) continue;
                if (f == 3 && !bad) continue;
                if (q.Length > 0 && msgs[i].Text.ToLowerInvariant().IndexOf(q) < 0 && trans[i].ToLowerInvariant().IndexOf(q) < 0) continue;
                view.Add(i);
            }
            busy = true; grid.RowCount = 0; grid.RowCount = view.Count; busy = false;
            if (view.Count > 0) { cur = 0; ShowCurrent(); } else cur = -1;
        }

        void GridValue(object s, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            int i = view[e.RowIndex]; BattleMsgs.Msg m = msgs[i];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = i; break;
                case 1: e.Value = m.Offset.ToString("X"); break;
                case 2: e.Value = trans[i].Length == 0 ? m.Length.ToString() : (used[i] < 0 ? "!" : used[i] + "/" + m.Length); break;
                case 3: e.Value = "«" + Show(m.Text) + "»"; break;
                case 4: e.Value = trans[i].Length == 0 ? "" : "«" + Show(trans[i]) + "»"; break;
            }
        }

        void GridFormat(object s, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            int i = view[e.RowIndex];
            if (trans[i].Length == 0 || (e.ColumnIndex != 2 && e.ColumnIndex != 4)) return;
            bool bad = used[i] < 0 || used[i] > msgs[i].Length;
            e.CellStyle.ForeColor = bad ? Theme.BadText : Theme.GoodText;
        }

        void ShowCurrent()
        {
            if (cur < 0 || cur >= view.Count) return;
            int i = view[cur]; BattleMsgs.Msg m = msgs[i];
            busy = true;
            info.Text = BattleMsgs.File + " @0x" + m.Offset.ToString("X") + "  ·  mensaje " + i + "  ·  hueco de " + m.Length + " bytes";
            origBox.Text = m.Text.Replace("\n", "\r\n");
            transBox.Text = trans[i].Replace("\n", "\r\n");
            busy = false;
            UpdateMeter(i);
        }

        void UpdateMeter(int i)
        {
            BattleMsgs.Msg m = msgs[i];
            if (trans[i].Length == 0) { meter.Set(0, m.Length, false, "sin traducir (" + m.Length + " bytes)"); return; }
            if (used[i] < 0) { meter.Set(m.Length, m.Length, true, "Carácter no válido"); return; }
            meter.Set(used[i], m.Length, used[i] > m.Length, used[i] + " / " + m.Length + " bytes" + (used[i] > m.Length ? "  ·  NO CABE: acorta " + (used[i] - m.Length) : ""));
        }

        /// <summary>Codifica y escribe un mensaje en el proyecto (o restaura el original si no cabe / esta vacio).</summary>
        void ApplyOne(int i)
        {
            BattleMsgs.Msg m = msgs[i];
            byte[] file = P.GetOriginal(BattleMsgs.File);
            if (trans[i].Length == 0) { used[i] = 0; P.Write(BattleMsgs.File, m.Offset, Slice(file, m.Offset, m.Length)); return; }
            string txt = trans[i] == "∅" ? "" : trans[i];
            used[i] = BattleMsgs.NeededBytes(P.Codec, file, txt, m.Prefix);
            byte[] enc = used[i] < 0 ? null : BattleMsgs.Encode(P.Codec, file, txt, m.Prefix, m.Length);
            if (enc == null) { if (used[i] >= 0 && used[i] <= m.Length) used[i] = m.Length + 1; P.Write(BattleMsgs.File, m.Offset, Slice(file, m.Offset, m.Length)); return; }
            P.Write(BattleMsgs.File, m.Offset, enc);
        }

        static byte[] Slice(byte[] b, int o, int n) { byte[] r = new byte[n]; Array.Copy(b, o, r, 0, n); return r; }

        void OnEdit()
        {
            if (cur < 0) return;
            int i = view[cur];
            trans[i] = transBox.Text.Replace("\r\n", "\n");
            ApplyOne(i);
            UpdateMeter(i);
            grid.InvalidateRow(cur);
        }

        public int Translated { get { int n = 0; foreach (string t in trans) if (t.Length > 0) n++; return n; } }

        /// <summary>Mensajes con traduccion que no caben (para el comprobador).</summary>
        public List<string> Problems()
        {
            List<string> l = new List<string>();
            for (int i = 0; i < msgs.Count; i++)
                if (trans[i].Length > 0 && (used[i] < 0 || used[i] > msgs[i].Length))
                    l.Add("Mensaje de combate " + i + ": " + (used[i] < 0 ? "carácter no válido" : "no cabe (" + used[i] + " de " + msgs[i].Length + " bytes)") + "  «" + Show(trans[i]) + "»");
            return l;
        }

        /// <summary>Carga mensajes_es.csv (id;offset;max_bytes;original;traduccion) y lo aplica al proyecto.</summary>
        public string LoadCsv(string path)
        {
            List<string[]> rows = Csv.Read(path); if (rows.Count < 2) return "El CSV está vacío.";
            int cid = Csv.Col(rows[0], "id"), ct = Csv.Col(rows[0], "traduccion");
            if (cid < 0 || ct < 0) return "Faltan columnas: se necesitan 'id' y 'traduccion'.";
            int ok = 0, bad = 0;
            for (int r = 1; r < rows.Count; r++)
            {
                string[] row = rows[r]; int id;
                if (row.Length <= ct || !int.TryParse(row[cid], out id) || id < 0 || id >= msgs.Count) continue;
                trans[id] = row[ct];
                ApplyOne(id);
                if (trans[id].Length > 0) { if (used[id] < 0 || used[id] > msgs[id].Length) bad++; else ok++; }
            }
            Refilter();
            return ok + " mensajes aplicados" + (bad > 0 ? ", " + bad + " no caben (filtro «No caben»)" : "");
        }

        public void SaveCsv(string path)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "id", "offset", "max_bytes", "original", "traduccion" });
            for (int i = 0; i < msgs.Count; i++) rows.Add(new string[] { i.ToString(), msgs[i].Offset.ToString("X"), msgs[i].Length.ToString(), msgs[i].Text, trans[i] });
            Csv.Write(path, rows, ';');
        }

        void AskImport()
        {
            if (P == null) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv", Title = "Importar mensajes de combate (mensajes_es.csv)" };
            if (d.ShowDialog() == DialogResult.OK) MessageBox.Show(LoadCsv(d.FileName), "Mensajes de combate");
        }

        void AskExport()
        {
            if (P == null) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "mensajes_es.csv" };
            if (d.ShowDialog() == DialogResult.OK) { try { SaveCsv(d.FileName); } catch (System.IO.IOException ex) { MessageBox.Show("No se pudo escribir (¿está abierto en Excel?):\r\n" + ex.Message); } }
        }
    }
}
