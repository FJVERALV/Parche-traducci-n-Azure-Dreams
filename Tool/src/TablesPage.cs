using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AzTool
{
    /// <summary>Editor de tablas de bytes (filas de tamano fijo) con plantillas de direcciones conocidas.</summary>
    public class TablesPage : Page
    {
        class Preset
        {
            public string Name, Note, File;
            public int Off, RowSize, Rows;
            public long Raw;            // si File == null: offset crudo del .bin
            public string[] Cols;
        }

        List<Preset> presets = new List<Preset>();
        ComboBox presetBox, fileBox;
        TextBox offBox;
        NumericUpDown rowSizeBox, rowsBox;
        CheckBox hexBox;
        DataGridView grid;
        Label info, note;
        string file; int baseOff, rowSize = 24, rows = 64;
        string[] colNames;
        bool busy;

        public override string Title { get { return "Tablas de datos"; } }

        public TablesPage()
        {
            presets.Add(new Preset { Name = "Personalizada", Note = "Elige archivo, offset, tamaño de fila y nº de filas.", File = RamMap.Slus, Off = 0x40968, RowSize = 24, Rows = 16 });
            presets.Add(new Preset
            {
                Name = "Stats iniciales de familiares (24 B/fila)", File = RamMap.Slus, Off = 0x40968, RowSize = 24, Rows = 64,
                Note = "Según adrando (RAM 0x8006D168). Una fila por familiar/monstruo. Sin verificar en juego: cambia un valor y compruébalo.",
                Cols = new string[] { "Ataque", "Defensa", "Agilidad", "Suerte", "MP", "HP", "XP dada", "+7", "Hech.1 id", "Hech.1 nv", "Hech.1 nv2", "Hech.2 id", "Hech.2 nv", "Hech.2 nv2", "Hech.3 id", "Hech.3 nv", "Hech.3 nv2", "Nivel", "IA", "ID", "Elemento", "Empujable", "Volador" }
            });
            presets.Add(new Preset
            {
                Name = "Trampas (12 B/fila)", File = RamMap.Slus, Off = 0x464D0, RowSize = 12, Rows = 20,
                Note = "RAM 0x80072CD0. Byte 1 contiene el peso/probabilidad de aparición (bits 4-5)."
            });
            presets.Add(new Preset
            {
                Name = "Crecimiento de stats (8 B/fila)", Raw = 0x1cb9c94, RowSize = 8, Rows = 64,
                Note = "Offset crudo 0x1CB9C94 (adrando), RAM 0x800DDCBC. Se puede ver también en el editor hex."
            });
            presets.Add(new Preset
            {
                Name = "Hechizo oculto por monstruo (1 B/fila)", Raw = 0x376318c, RowSize = 1, Rows = 64,
                Note = "Offset crudo 0x376318C (adrando). Índice = id de monstruo.",
                Cols = new string[] { "Hechizo" }
            });

            Panel bar = MakeBar(84);
            presetBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 320 };
            foreach (Preset p in presets) presetBox.Items.Add(p.Name);
            fileBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 340, Top = 10, Width = 200 };
            offBox = new TextBox { Left = 550, Top = 10, Width = 90, Text = "0" };
            Label l1 = Theme.MakeLabel("Tam. fila", true); l1.Left = 650; l1.Top = 14;
            rowSizeBox = new NumericUpDown { Left = 712, Top = 10, Width = 56, Minimum = 1, Maximum = 64, Value = 24 };
            Label l2 = Theme.MakeLabel("Filas", true); l2.Left = 780; l2.Top = 14;
            rowsBox = new NumericUpDown { Left = 820, Top = 10, Width = 64, Minimum = 1, Maximum = 4096, Value = 64 };
            hexBox = new CheckBox { Left = 900, Top = 12, Width = 90, Text = "Hexadecimal" };
            note = new Label { Left = 10, Top = 46, Width = 1100, Height = 32, ForeColor = Theme.Dim, BackColor = Color.Transparent };
            bar.Controls.AddRange(new Control[] { presetBox, fileBox, offBox, l1, rowSizeBox, l2, rowsBox, hexBox, note });

            grid = new DataGridView { Dock = DockStyle.Fill, VirtualMode = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ScrollBars = ScrollBars.Both };
            info = new Label { Dock = DockStyle.Bottom, Height = 28, ForeColor = Theme.Dim, Padding = new Padding(10, 6, 0, 0) };
            Controls.Add(grid); Controls.Add(info); Controls.Add(bar);
            Theme.Apply(this); bar.BackColor = Theme.Panel; note.BackColor = Color.Transparent;
            Theme.StyleGrid(grid); info.BackColor = Theme.Panel;
            grid.CellValueNeeded += ValueNeeded; grid.CellValuePushed += ValuePushed;
            grid.CellFormatting += Formatting;
            presetBox.SelectedIndexChanged += delegate { LoadPreset(); };
            fileBox.SelectedIndexChanged += delegate { if (!busy) Rebuild(); };
            offBox.Leave += delegate { if (!busy) Rebuild(); };
            offBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Rebuild(); e.SuppressKeyPress = true; } };
            rowSizeBox.ValueChanged += delegate { if (!busy) Rebuild(); };
            rowsBox.ValueChanged += delegate { if (!busy) Rebuild(); };
            hexBox.CheckedChanged += delegate { grid.Invalidate(); };
        }

        protected override void OnProject()
        {
            busy = true;
            fileBox.Items.Clear();
            foreach (IsoEntry e in P.Disc.ListFiles())
                if (!e.IsDir && !e.Path.EndsWith(".STR") && e.Size <= 40u * 1024 * 1024) fileBox.Items.Add(e.Path);
            busy = false;
            presetBox.SelectedIndex = 1;
        }

        void LoadPreset()
        {
            Preset p = presets[presetBox.SelectedIndex];
            string f = p.File; int off = p.Off;
            if (f == null && !P.RawToFile(p.Raw, out f, out off)) { note.Text = "No se pudo localizar el offset 0x" + p.Raw.ToString("X") + " en el disco."; return; }
            busy = true;
            int i = fileBox.Items.IndexOf(f); if (i >= 0) fileBox.SelectedIndex = i;
            offBox.Text = off.ToString("X"); rowSizeBox.Value = p.RowSize; rowsBox.Value = p.Rows;
            busy = false;
            colNames = p.Cols;
            note.Text = p.Note;
            Rebuild();
        }

        void Rebuild()
        {
            if (fileBox.SelectedItem == null) return;
            file = (string)fileBox.SelectedItem;
            long o; long.TryParse(offBox.Text.Trim().Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out o);
            baseOff = (int)o; rowSize = (int)rowSizeBox.Value; rows = (int)rowsBox.Value;
            byte[] d = P.Get(file);
            rows = Math.Max(0, Math.Min(rows, (d.Length - baseOff) / rowSize));
            grid.Columns.Clear(); grid.RowCount = 0;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "Fila", Width = 52, ReadOnly = true, Frozen = true });
            for (int c = 0; c < rowSize; c++)
            {
                string nm = (colNames != null && presets[presetBox.SelectedIndex].Cols != null && c < colNames.Length) ? colNames[c] : "+" + c;
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c" + c, HeaderText = nm, Width = nm.Length > 6 ? 76 : 52 });
            }
            grid.RowCount = rows;
            long ram = RamMap.ToRam(file, baseOff);
            info.Text = file + " @0x" + baseOff.ToString("X") + (ram != 0 ? "   RAM 0x" + ram.ToString("X8") : "") + "   ·   " + rows + " filas × " + rowSize + " bytes   ·   doble clic para editar (0–255)";
        }

        void ValueNeeded(object s, DataGridViewCellValueEventArgs e)
        {
            if (file == null || e.RowIndex < 0 || e.RowIndex >= rows) return;
            if (e.ColumnIndex == 0) { e.Value = e.RowIndex; return; }
            int o = baseOff + e.RowIndex * rowSize + (e.ColumnIndex - 1);
            byte[] d = P.Get(file); if (o >= d.Length) return;
            e.Value = hexBox.Checked ? d[o].ToString("X2") : d[o].ToString();
        }

        void ValuePushed(object s, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
            string t = (e.Value ?? "").ToString().Trim();
            int v;
            bool ok = hexBox.Checked ? int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out v) : int.TryParse(t, out v);
            if (!ok) { v = -1; }
            if (v < 0 || v > 255) { MessageBox.Show("Valor no válido (0–255)."); return; }
            P.Write(file, baseOff + e.RowIndex * rowSize + (e.ColumnIndex - 1), new byte[] { (byte)v });
        }

        void Formatting(object s, DataGridViewCellFormattingEventArgs e)
        {
            if (file == null || e.RowIndex < 0 || e.ColumnIndex < 1 || e.RowIndex >= rows) return;
            int o = baseOff + e.RowIndex * rowSize + (e.ColumnIndex - 1);
            byte[] cur = P.Get(file), org = P.GetOriginal(file);
            if (o < cur.Length && cur[o] != org[o]) { e.CellStyle.BackColor = Color.FromArgb(34, 60, 44); e.CellStyle.ForeColor = Theme.GoodText; }
        }
    }
}
