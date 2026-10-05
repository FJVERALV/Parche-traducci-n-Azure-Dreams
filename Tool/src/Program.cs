using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AzTool
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);
            if (args.Length >= 3 && args[0] == "--buildtest") return SelfTest.BuildTest(args[1], args[2]);
            if (args.Length >= 4 && args[0] == "--checkcsv") return SelfTest.CheckCsv(args[1], args[2], args[3]);
            if (args.Length >= 4 && args[0] == "--checkitems") return SelfTest.CheckItems(args[1], args[2], args[3]);
            if (args.Length >= 3 && args[0] == "--itemmap") return SelfTest.ItemMap(args[1], args[2]);
            if (args.Length >= 6 && args[0] == "--applyall") return SelfTest.ApplyAll(args[1], args[2], args[3], args[4], args[5]);
            if (args.Length >= 4 && args[0] == "--guitest")
            {
                // Prueba del flujo de la interfaz sin ventanas: cargar carpeta -> comprobar -> crear BIN.
                // Debe dar el mismo BIN que --applyall.
                Project p = new Project(); p.Disc = new Disc(args[1], false); p.BinPath = args[1];
                string mapf = Path.Combine(args[2], "charmap.txt");
                p.Codec.LoadCharmap(File.Exists(mapf) ? mapf : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt"));
                TextPage tp = new TextPage(); ItemsPage ip = new ItemsPage(); BattlePage bp = new BattlePage(); FontPage fp = new FontPage();
                BuildPage b = new BuildPage(tp, ip, bp, fp);
                foreach (Page pg in new Page[] { tp, ip, bp, fp, b }) pg.Attach(p);
                StringBuilder log = new StringBuilder();
                log.AppendLine(b.LoadFolder(args[2]));
                bool ok = b.Check(); log.AppendLine(b.Report);
                b.CreateBinTo(args[3]); log.AppendLine(b.Report);
                File.WriteAllText(Path.ChangeExtension(args[3], ".log"), log.ToString());
                return ok ? 0 : 1;
            }
            if (args.Length >= 3 && args[0] == "--exportmsgs")
            {
                // Exporta los mensajes comprimidos de combate/objetos (DUNGEON) a CSV para traducir.
                Project p = new Project(); p.Disc = new Disc(args[1], false); p.BinPath = args[1];
                p.Codec.LoadCharmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt"));
                BattleMsgs.Export(p, args[2]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--listfiles")
            {
                using (Disc disc = new Disc(args[1], false))
                    foreach (IsoEntry e in disc.ListFiles())
                        Console.WriteLine(e.Lba + "\t" + e.Size + "\t" + e.Path);
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--extractfile")
            {
                using (Disc disc = new Disc(args[1], false))
                {
                    IsoEntry e = disc.ListFiles().Find(x => string.Equals(x.Path, args[2], StringComparison.OrdinalIgnoreCase));
                    if (e == null) { Console.WriteLine("no encontrado: " + args[2]); return 1; }
                    byte[] data = disc.ReadFile(e);
                    File.WriteAllBytes(args[3], data);
                    Console.WriteLine("extraido " + args[3] + " (" + data.Length + " bytes)");
                }
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--extractraw")
            {
                // Extrae sectores CD completos (2352 bytes, con sync/cabecera) para archivos de video
                // Mode 2 Form 2 (STR/XA), formato que esperan ffmpeg (demuxer psxstr) y jPSXdec.
                using (Disc disc = new Disc(args[1], false))
                {
                    IsoEntry e = disc.ListFiles().Find(x => string.Equals(x.Path, args[2], StringComparison.OrdinalIgnoreCase));
                    if (e == null) { Console.WriteLine("no encontrado: " + args[2]); return 1; }
                    int sectors = (int)((e.Size + Disc.DATA - 1) / Disc.DATA);
                    using (FileStream outFs = new FileStream(args[3], FileMode.Create, FileAccess.Write))
                        for (int i = 0; i < sectors; i++)
                        {
                            byte[] raw = disc.ReadRaw(e.Lba + i);
                            outFs.Write(raw, 0, raw.Length);
                        }
                    Console.WriteLine("extraido " + args[3] + " (" + sectors + " sectores crudos)");
                }
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--makeppf")
            {
                // PPF 3.0 con las diferencias entre el BIN original y el traducido (ver Ppf.cs).
                string desc = args.Length >= 5 ? args[4] : "Azure Dreams (USA) - Traduccion ES";
                try { long n = Ppf.Make(args[1], args[2], args[3], desc); Console.WriteLine("PPF creado: " + args[3] + " (" + n + " bloques de cambios)"); return 0; }
                catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
            }
            if (args.Length >= 4 && args[0] == "--verifyppf")
            {
                string rep; bool ok = Ppf.Verify(args[1], args[2], args[3], out rep);
                Console.Write(rep);
                return ok ? 0 : 1;
            }
            if (args.Length >= 3 && args[0] == "--extractallstr")
            {
                using (Disc disc = new Disc(args[1], false))
                {
                    Directory.CreateDirectory(args[2]);
                    foreach (IsoEntry e in disc.ListFiles())
                    {
                        if (e.IsDir || !e.Path.ToUpperInvariant().EndsWith(".STR")) continue;
                        if (e.Path.ToUpperInvariant().Contains("DUMMY")) continue;
                        string outPath = System.IO.Path.Combine(args[2], e.Path.Replace('/', '_'));
                        int sectors = (int)((e.Size + Disc.DATA - 1) / Disc.DATA);
                        using (FileStream outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                            for (int i = 0; i < sectors; i++)
                            {
                                byte[] raw = disc.ReadRaw(e.Lba + i);
                                outFs.Write(raw, 0, raw.Length);
                            }
                        Console.WriteLine(outPath + " (" + sectors + " sectores, " + e.Size + " bytes nominales)");
                    }
                }
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--shots")
            {
                MainForm f = new MainForm(null);
                f.ShotDir = args[1];
                Application.Run(f);
                return 0;
            }
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            return 0;
        }
    }

    static class SelfTest
    {
        /// <summary>Aplica texto_es.csv y objetos_es.csv al disco y genera un BIN traducido (sin abrir la interfaz).</summary>
        /// <summary>Textos que el detector no encuentra (rodeados de datos): archivo;offset;max_bytes;original;traduccion.
        /// Se escriben en su sitio exacto, rellenados hasta max_bytes igual que el resto.</summary>
        public static string ApplyExtra(Project p, string csv)
        {
            if (!File.Exists(csv)) return "Textos extra: no hay " + Path.GetFileName(csv);
            List<string[]> rows = Csv.Read(csv); string[] h = rows[0];
            int cf = Csv.Col(h, "archivo"), co = Csv.Col(h, "offset"), cm = Csv.Col(h, "max_bytes"), ct = Csv.Col(h, "traduccion"), cc = Csv.Col(h, "codificacion");
            int ok = 0; StringBuilder bad = new StringBuilder();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i]; if (r.Length <= ct || r[ct].Trim().Length == 0) continue;
                int max = int.Parse(r[cm]);
                if (cc >= 0 && cc < r.Length && r[cc].Trim().ToLowerInvariant() == "ascii")
                {
                    // texto ASCII de un solo byte (p.ej. "NOW LOADING..." del ejecutable), relleno con 00
                    byte[] a = Encoding.ASCII.GetBytes(r[ct]);
                    if (a.Length + 1 > max) { bad.Append(" " + r[co]); continue; }
                    byte[] w = new byte[max]; Array.Copy(a, w, a.Length);
                    p.Write(r[cf].Trim(), Convert.ToInt32(r[co].Trim(), 16), w); ok++;
                    continue;
                }
                byte[] enc = p.Codec.Encode(r[ct]);
                if (enc.Length > max) enc = p.Codec.Encode(NoOpening(r[ct]));
                if (enc.Length > max) { bad.Append(" " + r[co]); continue; }
                p.Write(r[cf].Trim(), Convert.ToInt32(r[co].Trim(), 16), SjisCodec.PadTo(enc, max)); ok++;
            }
            return "Textos extra: aplicados=" + ok + (bad.Length > 0 ? " NO CABEN:" + bad : "");
        }

        public static int ApplyAll(string bin, string textCsv, string itemsCsv, string outBin, string report)
        {
            Project p = new Project(); p.Disc = new Disc(bin, false); p.BinPath = bin;
            p.Codec.LoadCharmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt"));
            StringBuilder sb = new StringBuilder();
            Dictionary<string, TextEntry> map = new Dictionary<string, TextEntry>();
            foreach (string f in new string[] { "SLUS_006.14", "MAIN/MAIN.BIN", "TOWN/TOWN.BIN", "DUNGEON/DUNGEON.BIN" })
                foreach (TextEntry e in p.Codec.Scan(p.GetOriginal(f), f, 3)) map[e.File + "|" + e.Offset.ToString("X")] = e;
            int tOk = 0, tOver = 0, tErr = 0;
            HashSet<string> owned = ItemsModel.OwnedTextOffsets(p);
            List<string[]> rows = Csv.Read(textCsv); string[] h = rows[0];
            int cf = Csv.Col(h, "archivo"), co = Csv.Col(h, "offset"), ct = Csv.Col(h, "traduccion"), cid = Csv.Col(h, "id");
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i]; if (r.Length <= ct || r[ct].Trim().Length == 0) continue;
                TextEntry t; if (!map.TryGetValue(r[cf].Trim() + "|" + Convert.ToInt64(r[co].Trim(), 16).ToString("X"), out t)) { tErr++; continue; }
                if (owned.Contains(t.File + "|" + t.Offset.ToString("X"))) { tOk++; continue; }   // cadena de objeto: la escribe la tabla de objetos
                byte[] enc;
                string tr = TextPage.NormalizeTrans(t, r[ct]);   // igual que la interfaz: sin saltos donde el original no tiene
                try { enc = p.Codec.Encode(tr); if (enc.Length > t.Length) enc = p.Codec.Encode(NoOpening(tr)); } catch (Exception ex) { tErr++; sb.AppendLine("texto " + r[cid] + " ERROR " + ex.Message); continue; }
                if (enc.Length > t.Length) { tOver++; sb.AppendLine("texto " + r[cid] + " NO CABE"); continue; }
                byte[] ob = new byte[t.Length]; Array.Copy(p.GetOriginal(t.File), (int)t.Offset, ob, 0, t.Length);
                SjisCodec.Current = r[cid];
                byte[] src = p.GetOriginal(t.File); int next = (int)t.Offset + t.Length < src.Length ? src[(int)t.Offset + t.Length] : -1;
                byte[] o = SjisCodec.PadEntry(t.File, ob, enc, next);
                if (o == null) { byte[] e2 = p.Codec.Encode(NoOpening(tr)); o = SjisCodec.PadEntry(t.File, ob, e2, next); }
                if (o == null) { tOver++; sb.AppendLine("texto " + r[cid] + " NO CABE (un mensaje interno no cabe en su hueco)"); continue; }
                p.Write(t.File, (int)t.Offset, o); tOk++;
            }
            sb.AppendLine("Texto: aplicadas=" + tOk + " se_pasan(omitidas)=" + tOver + " errores=" + tErr + " relleno_ultimo_recurso=" + SjisCodec.LastResort);
            sb.AppendLine(ApplyExtra(p, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(textCsv)), "textos_extra.csv")));
            sb.AppendLine(UiImages.Apply(p, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(textCsv)), "imagenes")));
            ItemsModel im = new ItemsModel(p); im.Load();
            int iOk = 0, iErr = 0;
            List<string[]> irows = Csv.Read(itemsCsv); string[] ih = irows[0];
            int cc = Csv.Col(ih, "cat_id"), cn = Csv.Col(ih, "num"), cnm = Csv.Col(ih, "nombre_es"), cd = Csv.Col(ih, "descripcion_es");
            for (int i = 1; i < irows.Count; i++)
            {
                string[] r = irows[i];
                ItemRec it = im.Items.Find(delegate(ItemRec x) { return x.Cat == int.Parse(r[cc]) && x.Index == int.Parse(r[cn]); });
                if (it == null) continue;
                if (r[cnm].Length > 0) { string e = im.SetString(it, false, r[cnm]); if (e == null) iOk++; else { iErr++; sb.AppendLine("objeto " + r[cc] + "|" + r[cn] + " nombre: " + e); } }
                if (r[cd].Length > 0) { string e = im.SetString(it, true, r[cd]); if (e == null) iOk++; else { iErr++; sb.AppendLine("objeto " + r[cc] + "|" + r[cn] + " desc: " + e); } }
            }
            sb.AppendLine("Objetos: aplicados=" + iOk + " errores=" + iErr + "  espacio libre restante=" + im.PoolFree() + " bytes");
            sb.AppendLine(BattleMsgs.Apply(p, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(textCsv)), "mensajes_es.csv")));
            if (p.Codec.HasExtra) sb.Append(FontPatch.ApplyAll(p));
            int runs = p.Build(outBin);
            sb.AppendLine("BIN creado: " + outBin + " (" + runs + " bloques)");
            foreach (string lr in SjisCodec.LastResortLog) sb.AppendLine("relleno ultimo recurso: texto " + lr);
            sb.AppendLine("paginas sin anclar: " + SjisCodec.UnanchoredPages + " en " + SjisCodec.PageFallbackLog.Count + " textos");
            foreach (string pf in SjisCodec.PageFallbackLog) sb.AppendLine("paginas sin anclar: texto " + pf);
            File.WriteAllText(report, sb.ToString());
            return iErr == 0 ? 0 : 1;
        }

        /// <summary>Lista, para cada objeto, en que archivo/offset esta su nombre y su descripcion (para no traducirlos dos veces).</summary>
        public static int ItemMap(string bin, string outFile)
        {
            Project p = new Project(); p.Disc = new Disc(bin, false); p.BinPath = bin;
            ItemsModel im = new ItemsModel(p); im.Load();
            StringBuilder sb = new StringBuilder();
            foreach (ItemRec it in im.Items)
            {
                byte[] ab = p.GetOriginal(it.File);
                for (int k = 0; k < 2; k++)
                {
                    long ptr = BitConverter.ToUInt32(ab, it.RecOff + (k == 0 ? 4 : 8));
                    string f; int off;
                    if (ptr != 0 && RamMap.ToFile(ptr, out f, out off)) sb.AppendLine(f + "\t" + off.ToString("X") + "\t" + it.Cat + "\t" + it.Index + "\t" + (k == 0 ? "N" : "D"));
                }
            }
            File.WriteAllText(outFile, sb.ToString());
            return 0;
        }

        /// <summary>Comprueba objetos_es.csv: que nombres y descripciones caben en su hueco y cuantos bytes de reubicacion hacen falta.</summary>
        /// <summary>Quita los signos de apertura (¿ ¡) para ganar 2 bytes por signo cuando la linea no cabe.</summary>
        public static string NoOpening(string s) { return s.Replace("\u00BF", "").Replace("\u00A1", ""); }

        public static int CheckItems(string bin, string csv, string outFile)
        {
            Project p = new Project(); p.Disc = new Disc(bin, false); p.BinPath = bin;
            p.Codec.LoadCharmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt"));
            ItemsModel im = new ItemsModel(p); im.Load();
            List<string[]> rows = Csv.Read(csv); string[] h = rows[0];
            int cc = Csv.Col(h, "cat_id"), cn = Csv.Col(h, "num"), cnm = Csv.Col(h, "nombre_es"), cd = Csv.Col(h, "descripcion_es");
            StringBuilder sb = new StringBuilder(); int ok = 0, over = 0, reloc = 0, err = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                ItemRec it = im.Items.Find(delegate(ItemRec x) { return x.Cat == int.Parse(r[cc]) && x.Index == int.Parse(r[cn]); });
                if (it == null) continue;
                for (int k = 0; k < 2; k++)
                {
                    string t = k == 0 ? r[cnm] : r[cd]; if (t.Length == 0) continue;
                    int need = im.NeededBytes(t), slot = im.CurrentSlot(it, k == 1);
                    if (need < 0) { err++; sb.AppendLine(r[cc] + "|" + r[cn] + (k == 0 ? " nombre" : " desc") + " ERROR caracter"); }
                    else if (need > slot) { over++; reloc += need + 4; sb.AppendLine(r[cc] + "|" + r[cn] + (k == 0 ? " nombre '" : " desc '") + t.Replace("\n", "/") + "' pasa " + (need - slot) + " bytes (" + need + ">" + slot + ")"); }
                    else ok++;
                }
            }
            sb.Insert(0, "caben=" + ok + " se_pasan=" + over + " errores=" + err + " bytes_reubicacion_aprox=" + reloc + " (libres " + im.PoolFree() + ")\r\n");
            File.WriteAllText(outFile, sb.ToString());
            return 0;
        }

        /// <summary>Comprueba un CSV de texto traducido: cuantas filas caben, cuales se pasan y cuales tienen errores. Escribe el informe en outFile.</summary>
        public static int CheckCsv(string bin, string csv, string outFile)
        {
            Project p = new Project(); p.Disc = new Disc(bin, false); p.BinPath = bin;
            p.Codec.LoadCharmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "charmap.txt"));
            Dictionary<string, TextEntry> map = new Dictionary<string, TextEntry>();
            foreach (string f in new string[] { "SLUS_006.14", "MAIN/MAIN.BIN", "TOWN/TOWN.BIN", "DUNGEON/DUNGEON.BIN" })
                foreach (TextEntry e in p.Codec.Scan(p.GetOriginal(f), f, 3)) map[e.File + "|" + e.Offset.ToString("X")] = e;
            List<string[]> rows = Csv.Read(csv);
            string[] h = rows[0];
            int cid = Csv.Col(h, "id"), cf = Csv.Col(h, "archivo"), co = Csv.Col(h, "offset"), ct = Csv.Col(h, "traduccion");
            StringBuilder sb = new StringBuilder();
            int total = 0, done = 0, fit = 0, over = 0, err = 0, tagBad = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i]; total++;
                if (r.Length <= ct || r[ct].Trim().Length == 0) continue;
                done++;
                long off = Convert.ToInt64(r[co].Trim(), 16); TextEntry t;
                if (!map.TryGetValue(r[cf].Trim() + "|" + off.ToString("X"), out t)) { err++; sb.AppendLine(r[cid] + "\tSIN_ENTRADA"); continue; }
                string tr = r[ct];
                try
                {
                    byte[] enc = p.Codec.Encode(tr);
                    if (enc.Length > t.Length) enc = p.Codec.Encode(NoOpening(tr));
                    string cmp = p.Codec.CompareControl(t.Original, tr);
                    if (cmp.Length > 0) { tagBad++; sb.AppendLine(r[cid] + "\tCODIGOS\t" + cmp); }
                    if (enc.Length > t.Length) { over++; sb.AppendLine(r[cid] + "\tSOBRA\t" + ((enc.Length - t.Length + 1) / 2) + "\tmax=" + t.MaxChars); } else fit++;
                }
                catch (Exception ex) { err++; sb.AppendLine(r[cid] + "\tERROR\t" + ex.Message); }
            }
            sb.Insert(0, "filas=" + total + " traducidas=" + done + " caben=" + fit + " se_pasan=" + over + " errores=" + err + " codigos_distintos=" + tagBad + "\r\n");
            File.WriteAllText(outFile, sb.ToString());
            return 0;
        }

        /// <summary>Genera un BIN de prueba con cambios visibles en objetos y en un texto, y lo verifica.</summary>
        public static int BuildTest(string bin, string outDir)
        {
            StringBuilder log = new StringBuilder();
            Directory.CreateDirectory(outDir);
            Project p = new Project(); p.Disc = new Disc(bin, false); p.BinPath = bin;
            ItemsModel im = new ItemsModel(p); im.Load();
            string[][] changes = {
                new string[] { "Hierbas", "1", "Hierba", "Hierba que restaura\nPV." },
                new string[] { "Hierbas", "2", "Antidoto", "Neutraliza el veneno." },
                new string[] { "Hierbas", "3", "Anticaos", "Cura el caos." },
                new string[] { "Espadas", "1", "Oro", "Cubierta de oro. ATQ 1." },
                new string[] { "Huevos", "2", "KEWNE", "Un huevo KEWNE." } };
            foreach (string[] c in changes)
            {
                ItemRec it = im.Items.Find(delegate(ItemRec x) { return x.CatName == c[0] && x.Index == int.Parse(c[1]); });
                if (it == null) { log.AppendLine("no encontrado " + c[0] + " " + c[1]); continue; }
                string e1 = im.SetString(it, false, c[2]), e2 = im.SetString(it, true, c[3]);
                log.AppendLine(c[0] + " " + c[1] + ": " + it.Name + " -> " + c[2] + "  [" + (e1 ?? "ok") + "/" + (e2 ?? "ok") + "]");
            }
            // un texto suelto del SLUS
            int ntext = 0;
            foreach (TextEntry t in p.Codec.Scan(p.GetOriginal(RamMap.Slus), RamMap.Slus, 3))
            {
                if (t.Original == "Kewne" && ntext == 0)
                {
                    byte[] enc = p.Codec.Encode("Kewne!"); byte[] o = new byte[t.Length]; Array.Copy(enc, o, Math.Min(enc.Length, o.Length));
                    for (int i = enc.Length; i + 1 < o.Length; i += 2) { o[i] = 0x81; o[i + 1] = 0x40; }
                    p.Write(RamMap.Slus, (int)t.Offset, o); ntext++;
                    log.AppendLine("texto @0x" + t.Offset.ToString("X") + ": Kewne -> Kewne!");
                }
            }
            string outBin = Path.Combine(outDir, "Azure Dreams (USA) [PRUEBA].bin");
            int runs = p.Build(outBin);
            log.AppendLine("BIN creado con " + runs + " bloques: " + outBin);
            // verificacion: reabrir y comprobar ECC en todos los sectores tocados y leer los objetos
            Project q = new Project(); q.Disc = new Disc(outBin, false); q.BinPath = outBin;
            ItemsModel im2 = new ItemsModel(q); im2.Load();
            int bad = 0;
            foreach (Patch pt in p.Diff())
            {
                IsoEntry e = q.Disc.ListFiles().Find(delegate(IsoEntry x) { return x.Path == pt.File; });
                for (long l = e.Lba + pt.Offset / 2048; l <= e.Lba + (pt.Offset + pt.Data.Length - 1) / 2048; l++) if (!q.Disc.VerifyEcc(l)) bad++;
            }
            log.AppendLine("Verificacion ECC de sectores modificados: " + bad + " errores");
            foreach (ItemRec it in im2.Items) if (it.Changed || (it.CatName == "Hierbas" && it.Index <= 3) || (it.CatName == "Espadas" && it.Index == 1) || (it.CatName == "Huevos" && it.Index == 2)) log.AppendLine("  releido: " + it.CatName + " " + it.Index + " = " + it.Name + " | " + it.Desc.Replace("\n", " / "));
            q.Disc.Dispose();
            File.WriteAllText(Path.Combine(outDir, "prueba.log"), log.ToString());
            return bad == 0 ? 0 : 1;
        }

        public static int Run(string bin)
        {
            StringBuilder log = new StringBuilder();
            int bad = 0;
            Project p = new Project();
            p.Disc = new Disc(bin, false); p.BinPath = bin;
            log.AppendLine("Sectores: " + p.Disc.SectorCount);
            int n = 0;
            foreach (IsoEntry e in p.Disc.ListFiles())
            {
                if (e.IsDir || e.Size == 0 || e.Path.StartsWith("STR")) continue;
                uint secs = (e.Size + 2047) / 2048, step = Math.Max(1u, secs / 50);
                for (uint s = 0; s < secs; s += step) { n++; if (!p.Disc.VerifyEcc(e.Lba + s)) { bad++; log.AppendLine("ECC MAL " + e.Path + " +" + s); } }
            }
            log.AppendLine("ECC verificados " + n + " errores " + bad);
            // CSV: ida y vuelta con casos dificiles
            string csvPath = Path.Combine(Path.GetTempPath(), "azt_test.csv");
            List<string[]> tr = new List<string[]>();
            tr.Add(new string[] { "id", "texto", "otro" });
            tr.Add(new string[] { "1", "a;b,c", "dice \"hola\"" });
            tr.Add(new string[] { "2", "linea1\nlinea2", " espacio" });
            tr.Add(new string[] { "3", "¿Qué tal, ñandú?", "" });
            foreach (char dl in new char[] { ';', ',' })
            {
                Csv.Write(csvPath, tr, dl);
                List<string[]> back = Csv.Read(csvPath);
                bool same = back.Count == tr.Count;
                for (int i = 0; same && i < tr.Count; i++) { if (back[i].Length != tr[i].Length) same = false; else for (int k = 0; k < tr[i].Length; k++) if (back[i][k] != tr[i][k]) same = false; }
                log.AppendLine("CSV ida y vuelta (sep '" + dl + "'): " + (same ? "OK" : "FALLO"));
                if (!same) bad++;
            }
            File.Delete(csvPath);
            {
                SjisCodec sc = new SjisCodec();
                string simp = sc.Simplify("¿Qué tal, ñandú? “Sí” — ¡Adiós!");
                bool okA = simp == "Que tal, nandu? \"Si\" - Adios!";
                bool encOk; try { sc.Encode("¿Qué tal, ñandú?"); encOk = true; } catch (Exception) { encOk = false; }
                log.AppendLine("Tildes: [" + simp + "] " + (okA && encOk ? "OK" : "FALLO"));
                if (!(okA && encOk)) bad++;
            }
            ItemsModel im = new ItemsModel(p); im.Load();
            log.AppendLine("Objetos: " + im.Items.Count);
            for (int i = 0; i < Math.Min(8, im.Items.Count); i++) log.AppendLine("  " + im.Items[i].CatName + " | " + im.Items[i].Name + " | " + im.Items[i].Desc.Replace("\n", " / ") + " | " + im.Items[i].Buy + "/" + im.Items[i].Sell);
            foreach (string c in new string[] { "Huevos", "Familiares", "Espadas" })
            {
                int k = 0;
                foreach (ItemRec it in im.Items) if (it.CatName == c && k++ < 3) log.AppendLine("  [" + c + "] " + it.Name + " | " + it.Desc.Replace("\n", " / "));
            }
            // prueba de edicion de objeto + reubicacion + build + verificacion
            ItemRec t = im.Items[0];
            string err1 = im.SetString(t, false, "Hierba");
            string err2 = im.SetString(t, true, "Hierba que restaura muchisimos PV al que la come.\nSabe fatal.");
            log.AppendLine("Edicion: " + err1 + " / " + err2 + " pool libre=" + im.PoolFree());
            string outBin = Path.Combine(Path.GetTempPath(), "azt_selftest.bin");
            int runs = p.Build(outBin);
            Project p2 = new Project(); p2.Disc = new Disc(outBin, false); p2.BinPath = outBin;
            ItemsModel im2 = new ItemsModel(p2); im2.Load();
            log.AppendLine("Tras build (" + runs + " parches): " + im2.Items[0].Name + " | " + im2.Items[0].Desc.Replace("\n", " / "));
            foreach (Patch pt in p.Diff())
            {
                IsoEntry e = p2.Disc.ListFiles().Find(delegate(IsoEntry x) { return x.Path == pt.File; });
                long lba0 = e.Lba + pt.Offset / 2048, lba1 = e.Lba + (pt.Offset + pt.Data.Length - 1) / 2048;
                for (long l = lba0; l <= lba1; l++) if (!p2.Disc.VerifyEcc(l)) { bad++; log.AppendLine("ECC MAL tras parche en sector " + l); }
            }
            p2.Disc.Dispose(); File.Delete(outBin); File.Delete(Path.ChangeExtension(outBin, ".cue"));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"), log.ToString());
            return bad == 0 ? 0 : 1;
        }
    }

    public class HomePage : Page
    {
        Label stats;
        Button bOpen;
        public event EventHandler OpenClicked;
        public override string Title { get { return "Inicio"; } }
        public HomePage()
        {
            Label t = new Label { Text = "Azure Dreams (USA)", Font = new Font("Segoe UI Semibold", 24f), ForeColor = Theme.Text, AutoSize = true, Left = 40, Top = 34 };
            Label s = new Label { Text = "Herramienta de traducción y edición para PlayStation (BIN/CUE)", ForeColor = Theme.Dim, AutoSize = true, Left = 44, Top = 84, Font = new Font("Segoe UI", 11f) };
            bOpen = Theme.MakeButton("Abrir imagen BIN…", 200, true); bOpen.Left = 44; bOpen.Top = 130; bOpen.Height = 40;
            bOpen.Click += delegate { if (OpenClicked != null) OpenClicked(this, EventArgs.Empty); };
            stats = new Label { Left = 44, Top = 200, AutoSize = true, Font = new Font("Segoe UI", 10.5f), ForeColor = Theme.Text };
            Label guide = new Label { Left = 44, Top = 360, AutoSize = true, ForeColor = Theme.Dim, Font = new Font("Segoe UI", 10f),
                Text = "Flujo de trabajo\r\n\r\n" +
                       "1.  Crear parche > Cargar carpeta en el editor: trae las CSV de la traducción.\r\n" +
                       "2.  Texto: diálogos y menús (avisa si no cabe o si una línea pasa de 31 caracteres).\r\n" +
                       "3.  Objetos: nombres, descripciones y precios; lo que no cabe se reubica solo.\r\n" +
                       "4.  Mensajes combate: mensajes comprimidos de la torre (daño, nivel, objetos...).\r\n" +
                       "5.  Fuente: dibuja letras nuevas (á é í ó ú ñ ¿ ¡...) y asígnalas a símbolos.\r\n" +
                       "6.  Imágenes / UI: catálogo de gráficos del juego; exporta/importa PNG.\r\n" +
                       "7.  Crear parche: comprobar, crear el BIN traducido y el parche PPF.\r\n\r\n" +
                       "El BIN original nunca se modifica. Tu trabajo se autoguarda al cerrar.\r\n" +
                       "Guía completa: DOCUMENTACION.md" };
            Controls.AddRange(new Control[] { t, s, bOpen, stats, guide });
            Theme.Apply(this);
            foreach (Control c in Controls) if (c is Label) c.BackColor = Color.Transparent;
            t.Font = new Font("Segoe UI Semibold", 24f); s.Font = new Font("Segoe UI", 11f); stats.Font = new Font("Segoe UI", 10.5f); guide.Font = new Font("Segoe UI", 10f);
            t.ForeColor = Theme.Text; s.ForeColor = Theme.Dim; guide.ForeColor = Theme.Dim;
        }
        public void Update(Project p, TextPage tp, string msg)
        {
            if (p == null) { stats.Text = "Ningún disco abierto."; return; }
            int tot = 0, tr = 0;
            foreach (TextEntry e in tp.Entries) if (!e.IsJapanese) tot++;
            foreach (TextEntry e in tp.Entries) if (e.Translation.Length > 0 && e.UsedBytes >= 0 && e.UsedBytes <= e.Length) tr++;
            stats.Text = "Disco:  " + Path.GetFileName(p.BinPath) + "\r\nCadenas de texto en inglés:  " + tot + "\r\nTraducidas:  " + tr +
                         "\r\nArchivos modificados:  " + p.DirtyCount + "\r\nCambios pendientes:  " + p.Diff().Count + " bloques";
        }
    }

    class MainForm : Form
    {
        Project P;
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        Panel content;
        Label status, dirtyLbl;
        List<NavButton> navs = new List<NavButton>();
        List<Page> pages = new List<Page>();
        HomePage home; TextPage textPage; ItemsPage itemsPage; TablesPage tablesPage; GfxPage gfxPage; HexPage hexPage; FilesPage filesPage;
        BattlePage battlePage; FontPage fontPage; BuildPage buildPage;
        Page current;
        public string ShotDir;   // modo interno de pruebas: guarda una captura de cada pagina y cierra

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        void TakeShots()
        {
            Directory.CreateDirectory(ShotDir);
            for (int i = 0; i < pages.Count; i++)
            {
                ShowPage(pages[i]); Application.DoEvents(); System.Threading.Thread.Sleep(400); Application.DoEvents();
                if (pages[i] == textPage)
                {
                    textPage.DebugShow(Environment.GetEnvironmentVariable("AZ_SEARCH"), Environment.GetEnvironmentVariable("AZ_TRANS"));
                    Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
                }
                using (Bitmap bm = new Bitmap(Width, Height))
                {
                    using (Graphics g = Graphics.FromImage(bm))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { PrintWindow(Handle, hdc, 2); }   // PW_RENDERFULLCONTENT: incluye RichTextBox
                        finally { g.ReleaseHdc(hdc); }
                    }
                    bm.Save(Path.Combine(ShotDir, "page" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            Close();
        }

        public MainForm(string autoOpen)
        {
            Text = "Azure Dreams (USA) — Herramienta de traducción";
            Width = 1360; Height = 820; MinimumSize = new Size(1240, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI;
            BuildUi();
            Load += delegate
            {
                string bin = autoOpen;
                if (bin == null) { string c = Path.GetFullPath(Path.Combine(baseDir, @"..\Juego\Azure Dreams (USA).bin")); if (File.Exists(c)) bin = c; }
                if (bin != null && File.Exists(bin)) OpenDisc(bin);
                if (ShotDir != null) BeginInvoke(new Action(TakeShots));
            };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (ShotDir == null) Autosave(); };
        }

        void BuildUi()
        {
            MenuStrip ms = new MenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColors()), BackColor = Theme.Panel, ForeColor = Theme.Text, Padding = new Padding(6, 4, 0, 4) };
            ToolStripMenuItem mFile = new ToolStripMenuItem("Archivo");
            mFile.DropDownItems.Add("Abrir BIN…", null, delegate { AskOpen(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Crear BIN traducido…", null, delegate { if (NeedDisc()) { ShowPage(buildPage); buildPage.CreateBin(); } });
            mFile.DropDownItems.Add("Crear parche PPF…", null, delegate { if (NeedDisc()) { ShowPage(buildPage); buildPage.CreatePpf(); } });
            mFile.DropDownItems.Add("Probar en DuckStation", null, delegate { buildPage.RunEmu(buildPage.LastBin == null ? null : Path.ChangeExtension(buildPage.LastBin, ".cue")); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Salir", null, delegate { Close(); });
            ToolStripMenuItem mProj = new ToolStripMenuItem("Proyecto");
            mProj.DropDownItems.Add("Guardar parches (.azpatch)…", null, delegate { SavePatches(); });
            mProj.DropDownItems.Add("Cargar parches…", null, delegate { LoadPatches(); });
            mProj.DropDownItems.Add(new ToolStripSeparator());
            mProj.DropDownItems.Add("Exportar texto a CSV (Excel)…", null, delegate { SaveTextCsv(false, false); });
            mProj.DropDownItems.Add("Exportar solo traducidas a CSV…", null, delegate { SaveTextCsv(true, false); });
            mProj.DropDownItems.Add("Exportar texto con japonés a CSV…", null, delegate { SaveTextCsv(false, true); });
            mProj.DropDownItems.Add("Importar texto desde CSV…", null, delegate { LoadTextCsv(); });
            mProj.DropDownItems.Add(new ToolStripSeparator());
            mProj.DropDownItems.Add("Exportar objetos a CSV…", null, delegate { SaveItemsCsv(); });
            mProj.DropDownItems.Add("Importar objetos desde CSV…", null, delegate { LoadItemsCsv(); });
            mProj.DropDownItems.Add(new ToolStripSeparator());
            mProj.DropDownItems.Add("Exportar texto a TSV…", null, delegate { SaveTsv(false); });
            mProj.DropDownItems.Add("Importar texto desde TSV…", null, delegate { LoadTsv(); });
            mProj.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem mAcc = new ToolStripMenuItem("Quitar tildes y ñ automáticamente") { Checked = true, CheckOnClick = true };
            mAcc.CheckedChanged += delegate { if (P != null) { P.Codec.StripAccents = mAcc.Checked; textPage.RefreshAll(); SetStatus(mAcc.Checked ? "Las tildes se convierten al equivalente sin tilde" : "Sin conversión: los caracteres no válidos darán error"); } };
            mProj.DropDownItems.Add(mAcc);
            mProj.DropDownItems.Add("Recargar charmap.txt", null, delegate { ReloadCharmap(); });
            foreach (ToolStripItem i in mFile.DropDownItems) { i.ForeColor = Theme.Text; i.BackColor = Theme.Panel; }
            foreach (ToolStripItem i in mProj.DropDownItems) { i.ForeColor = Theme.Text; i.BackColor = Theme.Panel; }
            mFile.ForeColor = Theme.Text; mProj.ForeColor = Theme.Text;
            ms.Items.Add(mFile); ms.Items.Add(mProj);
            MainMenuStrip = ms;

            Panel side = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Panel };
            Label brand = new Label { Dock = DockStyle.Top, Height = 64, Text = "  AZURE DREAMS\r\n  Translation Tool", ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 11f), TextAlign = ContentAlignment.MiddleLeft };
            Panel line = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

            home = new HomePage(); textPage = new TextPage(); itemsPage = new ItemsPage(); tablesPage = new TablesPage(); gfxPage = new GfxPage(); hexPage = new HexPage(); filesPage = new FilesPage();
            battlePage = new BattlePage(); fontPage = new FontPage();
            buildPage = new BuildPage(textPage, itemsPage, battlePage, fontPage);
            buildPage.CharmapChanged += delegate { CharmapReloaded(); };
            fontPage.CharmapSaved += delegate { CharmapReloaded(); };
            pages.AddRange(new Page[] { home, textPage, itemsPage, battlePage, fontPage, gfxPage, tablesPage, hexPage, filesPage, buildPage });
            string[] glyphs = { "⌂", "✎", "⚔", "⚡", "Ａ", "▦", "☰", "▤", "▣", "✔" };
            string[] names = { "Inicio", "Texto", "Objetos", "Mensajes combate", "Fuente", "Imágenes / UI", "Tablas de datos", "Editor hex", "Archivos", "Crear parche" };
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            for (int i = 0; i < pages.Count; i++)
            {
                NavButton nb = new NavButton(glyphs[i], names[i]);
                Page pg = pages[i];
                nb.Click += delegate { ShowPage(pg); };
                navs.Add(nb);
            }
            for (int i = navs.Count - 1; i >= 0; i--) side.Controls.Add(navs[i]);
            side.Controls.Add(line); side.Controls.Add(brand);
            // el ultimo Add queda arriba: brand, line, luego botones en orden

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Theme.Panel };
            status = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Dim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Text = "Listo" };
            dirtyLbl = new Label { Dock = DockStyle.Right, Width = 260, ForeColor = Theme.GoodText, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 10, 0) };
            bottom.Controls.Add(status); bottom.Controls.Add(dirtyLbl);

            Controls.Add(content); Controls.Add(side); Controls.Add(bottom); Controls.Add(ms);
            home.OpenClicked += delegate { AskOpen(); };
            filesPage.OpenInHex += delegate(object s, PathEventArgs e) { hexPage.ShowAt(e.Path, 0); ShowPage(hexPage); };
            ShowPage(home);
        }

        void ShowPage(Page pg)
        {
            if (current == pg) return;
            content.Controls.Clear();
            content.Controls.Add(pg);
            current = pg;
            for (int i = 0; i < pages.Count; i++) { navs[i].Selected = pages[i] == pg; navs[i].Invalidate(); }
            pg.OnShow();
            if (pg == home) home.Update(P, textPage, "");
        }

        void SetStatus(string s) { status.Text = s; Application.DoEvents(); }

        void AskOpen()
        {
            OpenFileDialog d = new OpenFileDialog { Filter = "Imagen BIN (*.bin)|*.bin|Todos|*.*", Title = "Abrir Azure Dreams (USA).bin" };
            if (d.ShowDialog() == DialogResult.OK) OpenDisc(d.FileName);
        }

        void OpenDisc(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                Project np = new Project();
                np.Disc = new Disc(path, false); np.BinPath = path;
                np.Codec.LoadCharmap(CharmapFile());
                if (P != null && P.Disc != null) P.Disc.Dispose();
                P = np;
                P.Changed += delegate { UpdateDirty(); };
                SetStatus("Cargando parches guardados…");
                string ap = Path.Combine(baseDir, "autosave.azpatch");
                if (File.Exists(ap)) { try { P.LoadPatches(ap); } catch (Exception) { } }
                SetStatus("Analizando texto…");
                foreach (Page pg in pages) if (pg != home) { SetStatus("Preparando " + pg.Title + "…"); pg.Attach(P); }
                string at = Path.Combine(baseDir, "autosave.tsv");
                if (File.Exists(at))
                {
                    try
                    {
                        textPage.LoadTsv(at);
                        if (textPage.LastUnmatched > 0)
                        {
                            string bak = Path.Combine(baseDir, "autosave_antiguo.tsv");
                            File.Copy(at, bak, true);
                            MessageBox.Show(textPage.LastUnmatched + " traducciones guardadas no coinciden con ninguna cadena porque la detección de textos ha cambiado " +
                                "(ahora los diálogos de varias páginas van juntos y con sus códigos).\r\n\r\nSe guardó una copia en:\r\n" + bak +
                                "\r\n\r\nPuedes recuperarlas desde esa copia (abre el TSV en Excel y pega la traducción en la cadena nueva).", "Traducciones antiguas");
                        }
                    }
                    catch (Exception) { }
                }
                SetStatus("Cargado: " + Path.GetFileName(path));
                UpdateDirty();
                home.Update(P, textPage, "");
                ShowPage(textPage);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error al abrir"); SetStatus("Error al abrir"); }
            finally { Cursor = Cursors.Default; }
        }

        void UpdateDirty()
        {
            dirtyLbl.Text = P == null ? "" : (P.DirtyCount == 0 ? "Sin cambios" : P.DirtyCount + " archivo(s) modificado(s)");
        }

        void Autosave()
        {
            if (P == null) return;
            try
            {
                P.SavePatches(Path.Combine(baseDir, "autosave.azpatch"));
                textPage.SaveTsv(Path.Combine(baseDir, "autosave.tsv"), true);
            }
            catch (Exception) { }
        }

        bool NeedDisc() { if (P == null) { MessageBox.Show("Abre primero el BIN."); return false; } return true; }

        void SavePatches()
        {
            if (!NeedDisc()) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "Parches (*.azpatch)|*.azpatch", FileName = "proyecto.azpatch" };
            if (d.ShowDialog() == DialogResult.OK) { P.SavePatches(d.FileName); SetStatus("Guardado " + d.FileName); }
        }

        void LoadPatches()
        {
            if (!NeedDisc()) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "Parches (*.azpatch)|*.azpatch" };
            if (d.ShowDialog() != DialogResult.OK) return;
            int n = P.LoadPatches(d.FileName);
            itemsPage.Reload(); textPage.RefreshAll();
            SetStatus(n + " bloques aplicados");
        }

        void SaveTsv(bool only)
        {
            if (!NeedDisc()) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "TSV (*.tsv)|*.tsv", FileName = only ? "traduccion.tsv" : "texto_completo.tsv" };
            if (d.ShowDialog() == DialogResult.OK) { textPage.SaveTsv(d.FileName, only); SetStatus("Guardado " + d.FileName); }
        }

        void SaveTextCsv(bool only, bool jp)
        {
            if (!NeedDisc()) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = only ? "traduccion.csv" : (jp ? "texto_con_japones.csv" : "texto.csv") };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { int n = textPage.SaveCsv(d.FileName, only, jp); SetStatus(n + " filas exportadas a " + d.FileName); }
            catch (IOException ex) { MessageBox.Show("No se pudo escribir (¿está abierto en Excel?):\r\n" + ex.Message); }
        }

        void LoadTextCsv()
        {
            if (!NeedDisc()) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv|Todos|*.*" };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { string msg = textPage.LoadCsv(d.FileName); SetStatus("CSV de texto importado"); MessageBox.Show(msg, "Importación"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error al importar"); }
        }

        void SaveItemsCsv()
        {
            if (!NeedDisc()) return;
            SaveFileDialog d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "objetos.csv" };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { int n = itemsPage.SaveCsv(d.FileName, false); SetStatus(n + " objetos exportados"); }
            catch (IOException ex) { MessageBox.Show("No se pudo escribir (¿está abierto en Excel?):\r\n" + ex.Message); }
        }

        void LoadItemsCsv()
        {
            if (!NeedDisc()) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv|Todos|*.*" };
            if (d.ShowDialog() != DialogResult.OK) return;
            try { string msg = itemsPage.LoadCsv(d.FileName); SetStatus("CSV de objetos importado"); MessageBox.Show(msg, "Importación"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error al importar"); }
        }

        void LoadTsv()
        {
            if (!NeedDisc()) return;
            OpenFileDialog d = new OpenFileDialog { Filter = "TSV (*.tsv)|*.tsv" };
            if (d.ShowDialog() != DialogResult.OK) return;
            int n = textPage.LoadTsv(d.FileName);
            SetStatus(n + " traducciones importadas");
        }

        /// <summary>charmap.txt de la carpeta de traduccion si existe; si no, el que va junto a la herramienta.</summary>
        string CharmapFile()
        {
            string f = Path.Combine(Settings.Get("carpeta", ""), "charmap.txt");
            return File.Exists(f) ? f : Path.Combine(baseDir, "charmap.txt");
        }

        void ReloadCharmap()
        {
            if (!NeedDisc()) return;
            P.Codec.LoadCharmap(CharmapFile());
            CharmapReloaded();
            SetStatus("charmap.txt recargado: " + P.Codec.CharmapPath);
        }

        void CharmapReloaded()
        {
            if (P == null) return;
            textPage.RebuildCharButtons(); textPage.RefreshAll(); fontPage.Attach(P);
        }
    }
}
