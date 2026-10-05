using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    /// <summary>Ajustes de la herramienta (ajustes.txt junto al .exe: clave=valor).</summary>
    public static class Settings
    {
        static string PathFile { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ajustes.txt"); } }
        static Dictionary<string, string> d;
        static void Load()
        {
            if (d != null) return;
            d = new Dictionary<string, string>();
            try { foreach (string l in File.ReadAllLines(PathFile, Encoding.UTF8)) { int i = l.IndexOf('='); if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1); } } catch (Exception) { }
        }
        public static string Get(string k, string def) { Load(); string v; return d.TryGetValue(k, out v) && v.Length > 0 ? v : def; }
        public static void Set(string k, string v)
        {
            Load(); d[k] = v;
            try { List<string> l = new List<string>(); foreach (KeyValuePair<string, string> kv in d) l.Add(kv.Key + "=" + kv.Value); File.WriteAllLines(PathFile, l.ToArray(), Encoding.UTF8); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Asistente para crear el parche: carpeta de traduccion (las CSV + charmap.txt) -> editor -> comprobar ->
    /// BIN traducido -> parche PPF verificado -> probar en el emulador. Hace lo mismo que la linea de comandos
    /// (--applyall, --makeppf, --verifyppf) pero incluyendo ademas lo editado en la herramienta (imagenes, fuente, hex).
    /// </summary>
    public class BuildPage : Page
    {
        public const string FText = "texto_es.csv", FItems = "objetos_es.csv", FMsgs = "mensajes_es.csv", FExtra = "textos_extra.csv", FMap = "charmap.txt";
        TextPage textPage; ItemsPage itemsPage; BattlePage battlePage; FontPage fontPage;
        TextBox folderBox, descBox, report;
        Label filesLbl;
        public string LastBin;
        public event EventHandler CharmapChanged;

        public override string Title { get { return "Crear parche"; } }

        public BuildPage(TextPage tp, ItemsPage ip, BattlePage bp, FontPage fp)
        {
            textPage = tp; itemsPage = ip; battlePage = bp; fontPage = fp;
            Panel top = new Panel { Dock = DockStyle.Top, Height = 300, BackColor = Theme.Panel, Padding = new Padding(16) };
            int y = 12;
            Label t1 = Theme.MakeLabel("CARPETA DE TRADUCCIÓN", true); t1.Left = 16; t1.Top = y; top.Controls.Add(t1); y += 22;
            folderBox = new TextBox { Left = 16, Top = y, Width = 640 };
            Button bPick = Theme.MakeButton("Elegir…", 80, false); bPick.Left = 664; bPick.Top = y - 2;
            Button bOpenF = Theme.MakeButton("Abrir carpeta", 110, false); bOpenF.Left = 752; bOpenF.Top = y - 2;
            top.Controls.AddRange(new Control[] { folderBox, bPick, bOpenF }); y += 34;
            filesLbl = new Label { Left = 16, Top = y, Width = 900, Height = 92, ForeColor = Theme.Text, Font = Theme.Mono }; top.Controls.Add(filesLbl); y += 96;
            Button bLoad = Theme.MakeButton("Cargar carpeta en el editor", 220, false); bLoad.Left = 16; bLoad.Top = y;
            Button bSaveF = Theme.MakeButton("Guardar el editor en la carpeta", 240, false); bSaveF.Left = 244; bSaveF.Top = y;
            top.Controls.AddRange(new Control[] { bLoad, bSaveF }); y += 44;
            Button b1 = Theme.MakeButton("1. Comprobar", 140, true); b1.Left = 16; b1.Top = y;
            Button b2 = Theme.MakeButton("2. Crear BIN traducido…", 190, true); b2.Left = 164; b2.Top = y;
            Button b3 = Theme.MakeButton("3. Crear parche PPF…", 180, true); b3.Left = 362; b3.Top = y;
            Button b4 = Theme.MakeButton("Probar en DuckStation", 170, false); b4.Left = 550; b4.Top = y;
            Label ld = Theme.MakeLabel("Descripción del PPF:", true); ld.Left = 734; ld.Top = y + 7; ld.AutoSize = true;
            descBox = new TextBox { Left = 866, Top = y + 3, Width = 300, Text = Settings.Get("ppf_desc", "Azure Dreams (USA) - Traduccion ES") };
            top.Controls.AddRange(new Control[] { b1, b2, b3, b4, ld, descBox });

            report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = Theme.Mono, BorderStyle = BorderStyle.None };
            report.Text = Help();
            Controls.Add(report); Controls.Add(top);
            Theme.Apply(this);
            top.BackColor = Theme.Panel; report.BackColor = Theme.Bg; report.ForeColor = Theme.Text;
            foreach (Control c in top.Controls) if (c is Label) c.BackColor = Color.Transparent;

            folderBox.Text = Settings.Get("carpeta", DefaultFolder());
            folderBox.Leave += delegate { Settings.Set("carpeta", folderBox.Text.Trim()); ShowFiles(); };
            descBox.Leave += delegate { Settings.Set("ppf_desc", descBox.Text); };
            bPick.Click += delegate
            {
                FolderBrowserDialog d = new FolderBrowserDialog { Description = "Carpeta con texto_es.csv, objetos_es.csv, mensajes_es.csv, textos_extra.csv y charmap.txt", SelectedPath = Folder };
                if (d.ShowDialog() == DialogResult.OK) { folderBox.Text = d.SelectedPath; Settings.Set("carpeta", d.SelectedPath); ShowFiles(); }
            };
            bOpenF.Click += delegate { if (Directory.Exists(Folder)) Process.Start("explorer.exe", "\"" + Folder + "\""); };
            bLoad.Click += delegate { LoadFolder(); };
            bSaveF.Click += delegate { SaveFolder(); };
            b1.Click += delegate { Check(); };
            b2.Click += delegate { CreateBin(); };
            b3.Click += delegate { CreatePpf(); };
            b4.Click += delegate { RunEmu(LastBin == null ? null : Path.ChangeExtension(LastBin, ".cue")); };
            ShowFiles();
        }

        static string DefaultFolder()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string c in new string[] { Path.Combine(b, "traduccion"), Path.Combine(b, @"..\Juego"), Path.Combine(b, @"..\traduccion") })
                if (File.Exists(Path.Combine(c, FText))) return Path.GetFullPath(c);
            return Path.Combine(b, "traduccion");
        }

        public string Folder { get { return folderBox.Text.Trim(); } }
        string F(string name) { return Path.Combine(Folder, name); }

        static string Help()
        {
            return "Cómo se crea el parche\r\n" +
                   "──────────────────────\r\n" +
                   "La carpeta de traducción contiene:\r\n" +
                   "  texto_es.csv      diálogos, menús y textos (columnas archivo, offset, traduccion)\r\n" +
                   "  objetos_es.csv    nombres, descripciones y precios de objetos\r\n" +
                   "  mensajes_es.csv   mensajes de combate y de la torre (formato comprimido)\r\n" +
                   "  textos_extra.csv  textos que el detector no encuentra (archivo, offset, max_bytes)\r\n" +
                   "  charmap.txt       letras nuevas de la fuente (á é í ó ú ü ñ Ñ ¿ ¡)\r\n\r\n" +
                   "1. «Cargar carpeta en el editor»: lleva las CSV a las pestañas Texto, Objetos y\r\n" +
                   "   Mensajes de combate, donde puedes revisarlas y editarlas.\r\n" +
                   "2. «Guardar el editor en la carpeta»: escribe lo editado de vuelta en las CSV.\r\n" +
                   "3. «Comprobar»: lista lo que no cabe, caracteres no válidos y líneas que se\r\n" +
                   "   saldrían de la ventana del juego (31 caracteres, 3 líneas).\r\n" +
                   "4. «Crear BIN traducido»: copia el BIN original y aplica todo (texto, objetos,\r\n" +
                   "   mensajes, textos extra, fuente, tabla de caracteres, imágenes y hex editados).\r\n" +
                   "5. «Crear parche PPF»: compara el BIN original con el traducido, crea el .ppf y\r\n" +
                   "   lo verifica aplicándolo en memoria. Ese .ppf es lo que se distribuye.\r\n\r\n" +
                   "El BIN original nunca se modifica.";
        }

        // ------------------------------------------------------------------ carpeta
        static int CountRows(string path, string col, out int filled)
        {
            filled = 0;
            try
            {
                List<string[]> r = Csv.Read(path); int c = Csv.Col(r[0], col);
                for (int i = 1; i < r.Count; i++) if (c >= 0 && c < r[i].Length && r[i][c].Trim().Length > 0) filled++;
                return r.Count - 1;
            }
            catch (Exception) { return -1; }
        }

        void ShowFiles()
        {
            StringBuilder sb = new StringBuilder();
            string[][] fs = { new[] { FText, "traduccion" }, new[] { FItems, "nombre_es" }, new[] { FMsgs, "traduccion" }, new[] { FExtra, "traduccion" } };
            foreach (string[] f in fs)
            {
                string p = F(f[0]);
                if (!File.Exists(p)) { sb.AppendLine("  ✗ " + f[0].PadRight(18) + "no existe"); continue; }
                int filled, n = CountRows(p, f[1], out filled);
                sb.AppendLine("  ✓ " + f[0].PadRight(18) + (n < 0 ? "no se puede leer" : n + " filas, " + filled + " con traducción"));
            }
            sb.AppendLine(File.Exists(F(FMap)) ? "  ✓ " + FMap.PadRight(18) + "letras nuevas de la fuente" : "  ✗ " + FMap.PadRight(18) + "no existe (se usa el de la herramienta)");
            filesLbl.Text = sb.ToString();
        }

        bool NeedP() { if (P == null) { MessageBox.Show("Abre primero el BIN original (Archivo > Abrir BIN)."); return false; } return true; }

        public void LoadFolder() { LoadFolder(Folder); }

        /// <summary>Lleva las CSV de una carpeta al editor (y charmap.txt a la fuente). Devuelve el informe.</summary>
        public string LoadFolder(string folder)
        {
            folderBox.Text = folder;
            if (!NeedP()) return null;
            if (!Directory.Exists(Folder)) { MessageBox.Show("No existe la carpeta:\r\n" + Folder); return null; }
            Cursor = Cursors.WaitCursor;
            StringBuilder sb = new StringBuilder("Carga de " + Folder + "\r\n\r\n");
            try
            {
                if (File.Exists(F(FMap))) { P.Codec.LoadCharmap(F(FMap)); sb.AppendLine("charmap.txt: " + P.Codec.Charmap().Count + " letras"); if (CharmapChanged != null) CharmapChanged(this, EventArgs.Empty); }
                if (File.Exists(F(FText))) sb.AppendLine("Texto: " + textPage.LoadCsv(F(FText)));
                if (File.Exists(F(FItems))) sb.AppendLine("Objetos: " + itemsPage.LoadCsv(F(FItems)));
                if (File.Exists(F(FMsgs))) sb.AppendLine("Mensajes de combate: " + battlePage.LoadCsv(F(FMsgs)));
                if (File.Exists(F(FExtra))) sb.AppendLine(SelfTest.ApplyExtra(P, F(FExtra)));
                sb.AppendLine("\r\nRevisa las pestañas y después pulsa «1. Comprobar».");
            }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex.Message); }
            finally { Cursor = Cursors.Default; }
            report.Text = sb.ToString();
            return report.Text;
        }

        public string Report { get { return report.Text; } }

        void SaveFolder()
        {
            if (!NeedP()) return;
            Directory.CreateDirectory(Folder);
            if (File.Exists(F(FText)) && MessageBox.Show("Se sobrescribirán " + FText + ", " + FItems + " y " + FMsgs + " en\r\n" + Folder + "\r\ncon lo que hay ahora en el editor. ¿Seguir?", "Guardar en la carpeta", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                int a = textPage.SaveCsv(F(FText), false, false);
                int b = itemsPage.SaveCsv(F(FItems), false);
                battlePage.SaveCsv(F(FMsgs));
                report.Text = "Guardado en " + Folder + ":\r\n  " + FText + " (" + a + " filas)\r\n  " + FItems + " (" + b + " filas)\r\n  " + FMsgs +
                              "\r\n\r\n" + FExtra + " y " + FMap + " no se tocan (edítalos a mano o en la pestaña Fuente).";
            }
            catch (IOException ex) { MessageBox.Show("No se pudo escribir (¿está abierto en Excel?):\r\n" + ex.Message); }
            ShowFiles();
        }

        // ------------------------------------------------------------------ comprobar
        public bool Check()
        {
            if (!NeedP()) return false;
            StringBuilder err = new StringBuilder(), warn = new StringBuilder();
            int nErr = 0, nWarn = 0, done = 0;
            foreach (TextEntry t in textPage.Entries)
            {
                if (t.Translation.Length == 0) continue;
                done++;
                string where = "Texto #" + t.Id + " (" + t.File + " 0x" + t.Offset.ToString("X") + "): ";
                if (t.UsedBytes == -2) { nErr++; err.AppendLine(where + "un mensaje interno ({01}) no cabe en su hueco"); }
                else if (t.UsedBytes < 0) { nErr++; err.AppendLine(where + "carácter no válido"); }
                else if (t.UsedBytes > t.Length) { nErr++; err.AppendLine(where + "no cabe (sobran " + ((t.UsedBytes - t.Length + 1) / 2) + " caracteres)"); }
                else
                {
                    if (t.Warn.Length > 0) { nWarn++; warn.AppendLine(where + t.Warn + "   «" + Short(t.Translation) + "»"); }
                    string cmp = P.Codec.CompareControl(t.Original, t.Translation);
                    if (cmp.Length > 0) { nWarn++; warn.AppendLine(where + "códigos distintos al original: " + cmp); }
                }
            }
            foreach (string s in battlePage.Problems()) { nErr++; err.AppendLine(s); }
            if (File.Exists(F(FExtra)))
                foreach (string[] r in SafeRows(F(FExtra)))
                    if (r.Length >= 5 && r[4].Trim().Length > 0)
                    {
                        int max; int.TryParse(r[2], out max);
                        try { int need = r[5].Trim().ToLowerInvariant() == "ascii" ? r[4].Length + 1 : P.Codec.Encode(SelfTest.NoOpening(r[4])).Length; if (need > max) { nErr++; err.AppendLine("Texto extra " + r[1] + ": no cabe en " + max + " bytes"); } }
                        catch (Exception ex) { nErr++; err.AppendLine("Texto extra " + r[1] + ": " + ex.Message); }
                    }
            if (P.Codec.HasExtra)
            {
                foreach (KeyValuePair<ushort, KeyValuePair<char, int>> e in P.Codec.Charmap())
                    if (e.Value.Value < 0 && FontPatch.DefaultCell(e.Key) < 0) { nErr++; err.AppendLine("charmap.txt: '" + e.Value.Key + "' no tiene celda (" + e.Key.ToString("X4") + "=" + e.Value.Key + "@celda)"); }
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Comprobación: " + done + " textos traducidos, " + battlePage.Translated + " mensajes de combate.");
            sb.AppendLine(nErr == 0 ? "✓ Sin errores." : "✗ " + nErr + " errores (no se aplicarán al juego hasta corregirlos):");
            sb.Append(err);
            sb.AppendLine();
            sb.AppendLine(nWarn == 0 ? "✓ Sin avisos de maquetación." : "⚠ " + nWarn + " avisos (se aplican, pero pueden verse cortados o raros en el juego):");
            sb.Append(warn);
            report.Text = sb.ToString();
            return nErr == 0;
        }

        static string Short(string s) { s = s.Replace("\n", "⏎"); return s.Length > 90 ? s.Substring(0, 90) + "…" : s; }
        static List<string[]> SafeRows(string p)
        {
            try { List<string[]> r = Csv.Read(p); string[] h = r[0]; int cf = Csv.Col(h, "archivo"), co = Csv.Col(h, "offset"), cm = Csv.Col(h, "max_bytes"), co2 = Csv.Col(h, "original"), ct = Csv.Col(h, "traduccion"), cc = Csv.Col(h, "codificacion");
                  List<string[]> o = new List<string[]>(); for (int i = 1; i < r.Count; i++) { string[] x = r[i]; Func<int, string> g = delegate(int c) { return c >= 0 && c < x.Length ? x[c] : ""; }; o.Add(new string[] { g(cf), g(co), g(cm), g(co2), g(ct), g(cc) }); } return o; }
            catch (Exception) { return new List<string[]>(); }
        }

        // ------------------------------------------------------------------ crear
        public void CreateBin()
        {
            if (!NeedP()) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "BIN (*.bin)|*.bin", Title = "Guardar BIN traducido", FileName = "Azure Dreams (USA) [ES].bin", InitialDirectory = Settings.Get("salida", Path.GetDirectoryName(P.BinPath)) };
            if (d.ShowDialog() != DialogResult.OK) return;
            if (Path.GetFullPath(d.FileName).Equals(Path.GetFullPath(P.BinPath), StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("No sobrescribas el BIN original."); return; }
            Settings.Set("salida", Path.GetDirectoryName(d.FileName));
            CreateBinTo(d.FileName);
        }

        public void CreateBinTo(string outBin)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Cursor = Cursors.WaitCursor;
                if (File.Exists(F(FExtra))) sb.AppendLine(SelfTest.ApplyExtra(P, F(FExtra)));
                if (Directory.Exists(F("imagenes"))) sb.AppendLine(UiImages.Apply(P, F("imagenes")));
                if (P.Codec.HasExtra) sb.Append(FontPatch.ApplyAll(P));
                int runs = P.Build(outBin);
                LastBin = outBin;
                sb.AppendLine("BIN creado: " + outBin + " (" + runs + " bloques de cambios) y su .cue.");
                sb.AppendLine("\r\nSiguiente paso: «3. Crear parche PPF» o «Probar en DuckStation».");
            }
            catch (IOException ex) { sb.AppendLine("ERROR: " + ex.Message + "\r\n(¿Está el BIN abierto en el emulador? Ciérralo o usa otro nombre.)"); }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex.Message); }
            finally { Cursor = Cursors.Default; }
            report.Text = sb.ToString();
        }

        public void CreatePpf()
        {
            if (!NeedP()) return;
            string bin = LastBin;
            if (bin == null || !File.Exists(bin))
            {
                OpenFileDialog o = new OpenFileDialog { Filter = "BIN traducido (*.bin)|*.bin", Title = "Elige el BIN traducido (creado en el paso 2)" };
                if (o.ShowDialog() != DialogResult.OK) return;
                bin = o.FileName;
            }
            SaveFileDialog d = new SaveFileDialog { Filter = "Parche PPF (*.ppf)|*.ppf", FileName = "AzureDreams_ES.ppf", InitialDirectory = Path.GetDirectoryName(bin) };
            if (d.ShowDialog() != DialogResult.OK) return;
            Settings.Set("ppf_desc", descBox.Text);
            try
            {
                Cursor = Cursors.WaitCursor;
                long n = Ppf.Make(P.BinPath, bin, d.FileName, descBox.Text);
                string rep; bool ok = Ppf.Verify(P.BinPath, d.FileName, bin, out rep);
                report.Text = "PPF creado: " + d.FileName + " (" + n + " bloques, " + (new FileInfo(d.FileName).Length / 1024) + " KB)\r\n\r\nVerificación:\r\n" + rep +
                              (ok ? "\r\nEste .ppf es lo que se publica. Se aplica con PPF-O-Matic, MultiPatch, etc. sobre el BIN original." : "");
            }
            catch (Exception ex) { report.Text = "ERROR: " + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        public void RunEmu(string cue)
        {
            if (cue == null || !File.Exists(cue))
            {
                OpenFileDialog o = new OpenFileDialog { Filter = "CUE (*.cue)|*.cue", Title = "Elige el .cue del BIN traducido" };
                if (o.ShowDialog() != DialogResult.OK) return;
                cue = o.FileName;
            }
            string emu = Settings.Get("emulador", "");
            if (!File.Exists(emu))
            {
                string b = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string c in new string[] { Path.Combine(b, @"..\Emulador\duckstation-qt-x64-ReleaseLTCG.exe"), Path.Combine(b, @"Emulador\duckstation-qt-x64-ReleaseLTCG.exe") })
                    if (File.Exists(c)) emu = c;
            }
            if (!File.Exists(emu))
            {
                OpenFileDialog o = new OpenFileDialog { Filter = "Emulador (*.exe)|*.exe", Title = "¿Dónde está el emulador (DuckStation, ePSXe...)?" };
                if (o.ShowDialog() != DialogResult.OK) return;
                emu = o.FileName; Settings.Set("emulador", emu);
            }
            Process.Start(emu, "\"" + cue + "\"");
        }
    }
}
