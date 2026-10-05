using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AzTool
{
    public class ItemsPage : Page
    {
        ItemsModel model;
        ListView list;
        ComboBox catFilter;
        TextBox search, nameBox, descBox;
        NumericUpDown buy, sell;
        MeterBar nameMeter, descMeter, poolMeter;
        Label info, warn;
        ItemRec sel;
        bool busy;

        public override string Title { get { return "Objetos, huevos y familiares"; } }

        public ItemsPage()
        {
            Panel bar = MakeBar(48);
            catFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 10, Width = 200 };
            search = new TextBox { Left = 220, Top = 10, Width = 240 };
            Label hint = Theme.MakeLabel("Nombres y descripciones se leen por punteros del juego; si no caben se reubican solos.", true); hint.Left = 476; hint.Top = 14;
            bar.Controls.AddRange(new Control[] { catFilter, search, hint });

            SplitContainer sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Theme.Border, SplitterWidth = 3, FixedPanel = FixedPanel.Panel2 };
            sc.SizeChanged += delegate { try { sc.SplitterDistance = Math.Max(300, sc.Width - 470); } catch (Exception) { } };
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, Font = Theme.UI };
            list.Columns.Add("Categoría", 104); list.Columns.Add("Nº", 32); list.Columns.Add("Nombre", 112); list.Columns.Add("Descripción", 170); list.Columns.Add("Compra", 54); list.Columns.Add("Venta", 46); list.Columns.Add("Máx N", 50); list.Columns.Add("Máx D", 50);
            list.SelectedIndexChanged += delegate { if (!busy) SelectItem(); };
            sc.Panel1.Controls.Add(list);

            Panel ed = sc.Panel2; ed.Padding = new Padding(14); ed.BackColor = Theme.Panel;
            info = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Theme.Dim, Text = "Selecciona un objeto" };
            Label l1 = Theme.MakeLabel("NOMBRE", true); l1.Dock = DockStyle.Top; l1.AutoSize = false; l1.Height = 22;
            nameBox = new TextBox { Dock = DockStyle.Top, Font = new Font("Consolas", 11f) };
            nameMeter = new MeterBar { Dock = DockStyle.Top, Height = 22 };
            Label l2 = Theme.MakeLabel("DESCRIPCIÓN   (Enter = salto de línea; ~30 car. por línea)", true); l2.Dock = DockStyle.Top; l2.AutoSize = false; l2.Height = 30; l2.Padding = new Padding(0, 10, 0, 0);
            descBox = new TextBox { Dock = DockStyle.Top, Height = 100, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = Theme.Mono };
            descMeter = new MeterBar { Dock = DockStyle.Top, Height = 22 };
            Panel prices = new Panel { Dock = DockStyle.Top, Height = 56 };
            Label lb = Theme.MakeLabel("Precio compra", true); lb.Left = 0; lb.Top = 18;
            buy = new NumericUpDown { Left = 96, Top = 14, Width = 80, Maximum = 65535 };
            Label ls = Theme.MakeLabel("Venta", true); ls.Left = 200; ls.Top = 18;
            sell = new NumericUpDown { Left = 246, Top = 14, Width = 80, Maximum = 65535 };
            prices.Controls.AddRange(new Control[] { lb, buy, ls, sell });
            warn = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = Theme.Warn };
            Label lp = Theme.MakeLabel("ESPACIO LIBRE PARA TEXTOS REUBICADOS", true); lp.Dock = DockStyle.Top; lp.AutoSize = false; lp.Height = 26; lp.Padding = new Padding(0, 6, 0, 0);
            poolMeter = new MeterBar { Dock = DockStyle.Top, Height = 22 };
            Button apply = Theme.MakeButton("Aplicar cambios", 140, true);
            Button revert = Theme.MakeButton("Deshacer", 100, false);
            FlowLayoutPanel fp = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 8, 0, 0) };
            fp.Controls.Add(apply); fp.Controls.Add(revert);
            apply.Click += delegate { ApplyCurrent(); };
            revert.Click += delegate { ShowSelected(); };
            ed.Controls.Add(warn); ed.Controls.Add(poolMeter); ed.Controls.Add(lp); ed.Controls.Add(fp); ed.Controls.Add(prices);
            ed.Controls.Add(descMeter); ed.Controls.Add(descBox); ed.Controls.Add(l2); ed.Controls.Add(nameMeter); ed.Controls.Add(nameBox); ed.Controls.Add(l1); ed.Controls.Add(info);
            // el orden de Dock: el ultimo agregado queda arriba
            nameBox.TextChanged += delegate { UpdateMeters(); };
            descBox.TextChanged += delegate { UpdateMeters(); };

            Controls.Add(sc); Controls.Add(bar);
            catFilter.SelectedIndexChanged += delegate { Fill(); };
            search.TextChanged += delegate { Fill(); };
            Theme.Apply(this);
            ed.BackColor = Theme.Panel; nameBox.BackColor = Theme.Panel2; descBox.BackColor = Theme.Panel2;
            foreach (Control c in ed.Controls) if (c is Label || c is FlowLayoutPanel || c is Panel) c.BackColor = Color.Transparent;
            bar.BackColor = Theme.Panel;
            list.BackColor = Theme.Bg; list.ForeColor = Theme.Text;
        }

        protected override void OnProject()
        {
            model = new ItemsModel(P);
            model.Load();
            catFilter.Items.Clear(); catFilter.Items.Add("(todas las categorías)");
            for (int i = 1; i <= 21; i++) catFilter.Items.Add(ItemsModel.CatNames[i]);
            catFilter.SelectedIndex = 0;
            Fill();
        }

        void Fill()
        {
            if (model == null) return;
            int cf = catFilter.SelectedIndex; // 0 = todas
            string q = search.Text.Trim().ToLowerInvariant();
            busy = true;
            list.BeginUpdate(); list.Items.Clear();
            foreach (ItemRec it in model.Items)
            {
                if (cf > 0 && it.Cat != cf) continue;
                if (q.Length > 0 && it.Name.ToLowerInvariant().IndexOf(q) < 0 && it.Desc.ToLowerInvariant().IndexOf(q) < 0) continue;
                ListViewItem li = new ListViewItem(it.CatName);
                li.SubItems.Add(it.Index.ToString()); li.SubItems.Add(it.Name);
                li.SubItems.Add(it.Desc.Replace("\n", " ⏎ ")); li.SubItems.Add(it.Buy.ToString()); li.SubItems.Add(it.Sell.ToString());
                li.SubItems.Add((model.CurrentSlot(it, false) / 2).ToString()); li.SubItems.Add((model.CurrentSlot(it, true) / 2).ToString());
                li.Tag = it;
                if (it.Changed) li.ForeColor = Theme.GoodText;
                list.Items.Add(li);
            }
            list.EndUpdate();
            busy = false;
            UpdatePool();
            if (list.Items.Count > 0) list.Items[0].Selected = true;
        }

        void SelectItem()
        {
            if (list.SelectedItems.Count == 0) return;
            sel = (ItemRec)list.SelectedItems[0].Tag;
            ShowSelected();
        }

        void ShowSelected()
        {
            if (sel == null) return;
            busy = true;
            info.Text = sel.CatName + "  ·  nº " + sel.Index + "  ·  flags 0x" + sel.Id.ToString("X2") + "  ·  registro " + sel.File + " @0x" + sel.RecOff.ToString("X6");
            nameBox.Text = sel.Name;
            descBox.Text = sel.Desc.Replace("\n", "\r\n");
            buy.Value = sel.Buy; sell.Value = sel.Sell;
            busy = false;
            UpdateMeters();
        }

        void UpdateMeters()
        {
            if (sel == null || busy) return;
            Meter(nameMeter, nameBox.Text, model.CurrentSlot(sel, false));
            Meter(descMeter, descBox.Text, model.CurrentSlot(sel, true));
            string w = "";
            if (sel.NameShared || sel.DescShared) w += "Este texto lo comparten varios registros. ";
            foreach (string line in descBox.Text.Replace("\r\n", "\n").Split('\n')) if (line.Length > 30) { w += "Una línea supera 30 caracteres. "; break; }
            warn.Text = w;
        }

        void Meter(MeterBar m, string text, int slot)
        {
            int need = model.NeededBytes(text);
            if (need < 0) { m.Set(1, 1, true, "Carácter no válido en el juego"); return; }
            bool over = need > slot;
            m.Set(need, slot, false, ((need + 1) / 2) + " / " + (slot / 2) + " caracteres  (" + need + " / " + slot + " bytes)" + (over ? "  · se reubicará" : ""));
        }

        void UpdatePool()
        {
            if (model == null) return;
            int free = model.PoolFree(), total = ItemsModel.PoolEnd - ItemsModel.PoolStart;
            poolMeter.Set(total - free, total, free < 20, (total - free) + " / " + total + " bytes usados");
        }

        void ApplyCurrent()
        {
            if (sel == null) return;
            string err = null;
            string n = nameBox.Text.Replace("\r\n", "\n"), d = descBox.Text.Replace("\r\n", "\n");
            if (n != sel.Name) { err = model.SetString(sel, false, n); if (err == null) sel.Name = n; }
            if (err == null && d != sel.Desc) { err = model.SetString(sel, true, d); if (err == null) sel.Desc = d; }
            if (err != null) { MessageBox.Show(err, "No se pudo aplicar"); return; }
            model.SetPrices(sel, (int)buy.Value, (int)sell.Value);
            sel.Buy = (int)buy.Value; sel.Sell = (int)sell.Value;
            sel.Changed = true;
            if (list.SelectedItems.Count > 0)
            {
                ListViewItem li = list.SelectedItems[0];
                li.SubItems[2].Text = sel.Name; li.SubItems[3].Text = sel.Desc.Replace("\n", " ⏎ ");
                li.SubItems[4].Text = sel.Buy.ToString(); li.SubItems[5].Text = sel.Sell.ToString();
                li.SubItems[6].Text = (model.CurrentSlot(sel, false) / 2).ToString(); li.SubItems[7].Text = (model.CurrentSlot(sel, true) / 2).ToString();
                li.ForeColor = Theme.GoodText;
            }
            UpdatePool(); UpdateMeters();
        }

        public void Reload() { if (P != null) OnProject(); }

        public int SaveCsv(string path, bool onlyChanged)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "cat_id", "categoria", "num", "nombre", "descripcion", "compra", "venta", "max_car_nombre", "max_car_desc", "nombre_es", "descripcion_es", "compra_es", "venta_es" });
            // columnas "nombre"/"descripcion" siempre del disco original; "_es" solo si cambian
            ItemsModel om = new ItemsModel(P) { UseOriginal = true }; om.Load();
            foreach (ItemRec it in model.Items)
            {
                ItemRec o = om.Items.Find(delegate(ItemRec x) { return x.Cat == it.Cat && x.Index == it.Index; }) ?? it;
                bool nc = it.Name != o.Name, dc = it.Desc != o.Desc, pc = it.Buy != o.Buy || it.Sell != o.Sell;
                if (onlyChanged && !(nc || dc || pc)) continue;
                rows.Add(new string[] { it.Cat.ToString(), it.CatName, it.Index.ToString(), o.Name, o.Desc, o.Buy.ToString(), o.Sell.ToString(),
                    (om.CurrentSlot(o, false) / 2).ToString(), (om.CurrentSlot(o, true) / 2).ToString(),
                    nc ? it.Name : "", dc ? it.Desc : "", pc ? it.Buy.ToString() : "", pc ? it.Sell.ToString() : "" });
            }
            Csv.Write(path, rows, Csv.ExcelEs);
            return rows.Count - 1;
        }

        public string LoadCsv(string path)
        {
            List<string[]> rows = Csv.Read(path);
            if (rows.Count < 2) return "El CSV está vacío.";
            string[] h = rows[0];
            int cc = Csv.Col(h, "cat_id"), cn = Csv.Col(h, "num"), cnm = Csv.Col(h, "nombre_es"), cd = Csv.Col(h, "descripcion_es"), cb = Csv.Col(h, "compra_es"), cs = Csv.Col(h, "venta_es");
            if (cc < 0 || cn < 0) return "Faltan las columnas 'cat_id' y 'num'.";
            int ok = 0, miss = 0;
            List<string> errs = new List<string>();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                int cat, num;
                if (r.Length <= Math.Max(cc, cn) || !int.TryParse(r[cc], out cat) || !int.TryParse(r[cn], out num)) continue;
                ItemRec it = model.Items.Find(delegate(ItemRec x) { return x.Cat == cat && x.Index == num; });
                if (it == null) { miss++; continue; }
                bool touched = false;
                string ne = cnm >= 0 && cnm < r.Length ? r[cnm] : "", de = cd >= 0 && cd < r.Length ? r[cd] : "";
                if (ne.Length > 0 && ne != it.Name) { string e = model.SetString(it, false, ne); if (e == null) { it.Name = ne; touched = true; } else errs.Add(it.CatName + " " + num + " (nombre): " + e); }
                if (de.Length > 0 && de != it.Desc) { string e = model.SetString(it, true, de); if (e == null) { it.Desc = de; touched = true; } else errs.Add(it.CatName + " " + num + " (descripción): " + e); }
                int b, s;
                bool hb = cb >= 0 && cb < r.Length && int.TryParse(r[cb], out b), hs = cs >= 0 && cs < r.Length && int.TryParse(r[cs], out s);
                if (hb || hs)
                {
                    int nb = hb ? int.Parse(r[cb]) : it.Buy, ns = hs ? int.Parse(r[cs]) : it.Sell;
                    if (nb != it.Buy || ns != it.Sell) { model.SetPrices(it, nb, ns); it.Buy = nb; it.Sell = ns; touched = true; }
                }
                if (touched) { it.Changed = true; ok++; }
            }
            Fill();
            string msg = ok + " objetos actualizados";
            if (errs.Count > 0) msg += ", " + errs.Count + " con error:\r\n" + string.Join("\r\n", errs.GetRange(0, Math.Min(8, errs.Count)).ToArray());
            if (miss > 0) msg += "\r\n" + miss + " filas no coinciden con ningún objeto.";
            return msg;
        }
    }
}
