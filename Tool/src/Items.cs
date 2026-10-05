using System;
using System.Collections.Generic;
using System.Text;

namespace AzTool
{
    public class ItemRec
    {
        public int Cat;
        public string CatName;
        public int Index;
        public string File;     // archivo donde esta el registro
        public int RecOff;      // offset del registro (20 bytes)
        public int Id;
        public int Buy, Sell;
        public string Name = "", Desc = "";
        public bool NameShared, DescShared;
        public bool Changed;
    }

    /// <summary>Lee y escribe las tablas de objetos (SLUS_006.14 / MAIN.BIN) siguiendo los punteros del juego.</summary>
    public class ItemsModel
    {
        public const long CatTableRam = 0x80073414;
        public const int RecSize = 20;

        public static readonly string[] CatNames = {
            "", "Hierbas", "Comida de familiar", "Semillas", "Bolas", "Pergaminos", "Cristales", "Campanas",
            "Gafas", "Lupas", "Arena", "Regalos", "Especiales", "Misiones", "Monedas", "Espadas", "Varitas",
            "Escudos", "Huevos", "Familiares", "Ascensores", "Trampas" };

        Project P;
        public List<ItemRec> Items = new List<ItemRec>();

        public ItemsModel(Project p) { P = p; }

        /// <summary>true = leer los nombres del disco original (para exportar la columna 'original').</summary>
        public bool UseOriginal;
        byte[] Data(string f) { return UseOriginal ? P.GetOriginal(f) : P.Get(f); }

        static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }

        public string ReadStringAt(long ram, out int slotLen)
        {
            slotLen = 0;
            string f; int off;
            if (ram == 0 || !RamMap.ToFile(ram, out f, out off)) return null;
            byte[] b = Data(f);
            int e = off;
            while (e < b.Length && b[e] != 0 && e - off < 600) e++;
            slotLen = e - off;
            return P.Codec.Decode(b, off, slotLen);
        }

        public void Load()
        {
            Items.Clear();
            byte[] slus = Data(RamMap.Slus);
            string f0; int t;
            RamMap.ToFile(CatTableRam, out f0, out t);
            Dictionary<long, int> nameUse = new Dictionary<long, int>();
            Dictionary<long, int> descUse = new Dictionary<long, int>();
            for (int c = 1; c <= 21; c++)
            {
                int p = t + c * 20;
                int rows = slus[p + 2];
                long arr = U32(slus, p + 12);
                string af; int ao;
                if (rows == 0 || !RamMap.ToFile(arr, out af, out ao)) continue;
                byte[] ab = Data(af);
                for (int r = 0; r < rows; r++)
                {
                    int q = ao + r * RecSize;
                    if (q + RecSize > ab.Length) break;
                    long np = U32(ab, q + 4), dp = U32(ab, q + 8);
                    int sl;
                    string name = ReadStringAt(np, out sl);
                    if (name == null || name.Length == 0) continue;
                    string desc = ReadStringAt(dp, out sl) ?? "";
                    ItemRec it = new ItemRec { Cat = c, CatName = CatNames[c], Index = r, File = af, RecOff = q, Id = ab[q], Name = name, Desc = desc, Buy = U16(ab, q + 16), Sell = U16(ab, q + 18) };
                    Items.Add(it);
                    int v; nameUse.TryGetValue(np, out v); nameUse[np] = v + 1;
                    descUse.TryGetValue(dp, out v); descUse[dp] = v + 1;
                }
            }
            foreach (ItemRec it in Items)
            {
                byte[] ab = Data(it.File);
                it.NameShared = nameUse[U32(ab, it.RecOff + 4)] > 1;
                it.DescShared = descUse[U32(ab, it.RecOff + 8)] > 1;
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Reparto del espacio de las cadenas de objetos.
        //
        // OJO: la antigua "zona libre" del SLUS (RAM 0x8007BCB0-0x8007BEF0) NO esta libre: es la pila de
        // matrices de la libreria grafica (PushMatrix/PopMatrix, 20 matrices de 32 bytes desde 0x8007BC70).
        // En escenas 3D el juego escribia matrices encima de los nombres reubicados y se colgaba (tienda de Fur).
        //
        // Ahora solo se usa espacio que pertenece a los propios textos de objetos:
        //   - cada cadena original ocupa su "hueco": desde su inicio hasta el siguiente byte no nulo del original
        //     (incluye el relleno de alineacion);
        //   - una traduccion igual a otra ya escrita reutiliza esa cadena (p.ej. los tres "Fuego");
        //   - lo que no cabe en su hueco va a la parte libre de otro hueco (colas de descripciones mas cortas,
        //     o huecos abandonados), nunca a uno al que apunte algun puntero ajeno a la tabla de objetos.
        // ---------------------------------------------------------------------------------------------
        class Span { public string File; public int Start, End; public bool Pinned; }
        List<Span> spans;

        void BuildSpans()
        {
            if (spans != null) return;
            spans = new List<Span>();
            ItemsModel om = new ItemsModel(P) { UseOriginal = true }; om.Load();
            Dictionary<long, int> itemRefs = new Dictionary<long, int>();
            foreach (ItemRec it in om.Items)
                for (int k = 0; k < 2; k++)
                {
                    long ptr = U32(P.GetOriginal(it.File), it.RecOff + (k == 0 ? 4 : 8));
                    int c; itemRefs.TryGetValue(ptr, out c); itemRefs[ptr] = c + 1;
                }
            // referencias de 32 bits alineadas en SLUS y MAIN (tablas): si hay mas que las de los objetos, la cadena
            // la usa algo mas y su hueco no se puede reutilizar para otra cosa
            Dictionary<long, int> allRefs = new Dictionary<long, int>();
            foreach (string g in new string[] { RamMap.Slus, RamMap.Main })
            {
                if (P.Find(g) == null) continue;
                byte[] gb = P.GetOriginal(g);
                for (int i = 0; i + 4 <= gb.Length; i += 4)
                {
                    long w = U32(gb, i);
                    if (itemRefs.ContainsKey(w)) { int c; allRefs.TryGetValue(w, out c); allRefs[w] = c + 1; }
                }
            }
            foreach (KeyValuePair<long, int> kv in itemRefs)
            {
                string f; int off;
                if (kv.Key == 0 || !RamMap.ToFile(kv.Key, out f, out off)) continue;
                byte[] b = P.GetOriginal(f);
                int e = off; while (e < b.Length && b[e] != 0) e++;
                while (e < b.Length && b[e] == 0) e++;
                int all; allRefs.TryGetValue(kv.Key, out all);
                spans.Add(new Span { File = f, Start = off, End = e, Pinned = all > kv.Value });
            }
        }

        /// <summary>Offsets (archivo|offset hex) de las cadenas de objetos del original: el texto de esas
        /// posiciones lo gestiona la tabla de objetos (la pestaña Texto no debe escribirlas).</summary>
        public static HashSet<string> OwnedTextOffsets(Project p)
        {
            HashSet<string> h = new HashSet<string>();
            ItemsModel om = new ItemsModel(p) { UseOriginal = true }; om.Load();
            foreach (ItemRec it in om.Items)
                for (int k = 0; k < 2; k++)
                {
                    string f; int off;
                    if (RamMap.ToFile(U32(p.GetOriginal(it.File), it.RecOff + (k == 0 ? 4 : 8)), out f, out off)) h.Add(f + "|" + off.ToString("X"));
                }
            return h;
        }

        /// <summary>Cadenas que hay ahora en uso: (archivo, inicio, fin exclusivo con su 00), menos el campo 'except'.</summary>
        List<int[]> Occupied(string file, ItemRec except, bool exceptDesc)
        {
            List<int[]> occ = new List<int[]>();
            foreach (ItemRec it in Items)
                for (int k = 0; k < 2; k++)
                {
                    if (it == except && (k == 1) == exceptDesc) continue;
                    string f; int off;
                    if (!RamMap.ToFile(U32(P.Get(it.File), it.RecOff + (k == 0 ? 4 : 8)), out f, out off) || f != file) continue;
                    byte[] b = P.Get(f); int e = off; while (e < b.Length && b[e] != 0) e++;
                    occ.Add(new int[] { off, e + 1 });
                }
            foreach (Span s in spans)       // las cadenas con punteros ajenos se quedan siempre en su sitio
                if (s.Pinned && s.File == file)
                {
                    byte[] b = P.Get(file); int e = s.Start; while (e < b.Length && b[e] != 0) e++;
                    occ.Add(new int[] { s.Start, e + 1 });
                }
            return occ;
        }

        static bool Free(List<int[]> occ, int a, int b)
        {
            foreach (int[] o in occ) if (a < o[1] && o[0] < b) return false;
            return true;
        }

        /// <summary>Busca 'need' bytes libres dentro de los huecos de objetos (posicion par). Devuelve (archivo, offset) o null.</summary>
        string FindFree(int need, ItemRec it, bool isDesc, out int at)
        {
            at = -1;
            foreach (string file in new string[] { RamMap.Slus, RamMap.Main })
            {
                List<int[]> occ = Occupied(file, it, isDesc);
                foreach (Span s in spans)
                {
                    if (s.File != file) continue;
                    for (int a = (s.Start + 1) & ~1; a + need <= s.End; a += 2)
                        if (Free(occ, a, a + need)) { at = a; return file; }
                }
            }
            return null;
        }

        public int PoolFree()
        {
            BuildSpans();
            int total = 0;
            foreach (string file in new string[] { RamMap.Slus, RamMap.Main })
            {
                List<int[]> occ = Occupied(file, null, false);
                foreach (Span s in spans) if (s.File == file) for (int a = s.Start; a < s.End; a++) if (Free(occ, a, a + 1)) total++;
            }
            return total;
        }

        /// <summary>Escribe un texto (nombre o descripcion). Si no cabe en su hueco lo reubica y corrige el puntero.</summary>
        public string SetString(ItemRec it, bool isDesc, string text)
        {
            BuildSpans();
            byte[] ab = P.Get(it.File);
            int ptrOff = it.RecOff + (isDesc ? 8 : 4);
            long ptr = U32(ab, ptrOff);
            string f; int off;
            if (!RamMap.ToFile(ptr, out f, out off)) return "Puntero invalido";
            byte[] enc;
            try { enc = P.Codec.Encode(text.Replace("\r\n", "\n")); }
            catch (Exception ex) { return ex.Message; }
            int need = enc.Length + 1;
            byte[] cur = P.Get(f);
            int oldEnd = off; while (oldEnd < cur.Length && cur[oldEnd] != 0) oldEnd++;
            bool same = oldEnd - off == enc.Length;
            for (int i = 0; same && i < enc.Length; i++) if (cur[off + i] != enc[i]) same = false;
            if (same) return null;

            List<int[]> occAll = Occupied(f, it, isDesc);
            // otros campos que usan esta MISMA cadena (compartida desde el original): se puede escribir en su sitio
            bool sharedNow = occAll.Exists(delegate(int[] o) { return o[0] == off; });
            List<int[]> occ = occAll.FindAll(delegate(int[] o) { return o[0] != off; });

            // 1) misma traduccion ya escrita en otro objeto: se comparte
            foreach (ItemRec o in Items)
                for (int k = 0; k < 2; k++)
                {
                    if (o == it && (k == 1) == isDesc) continue;
                    long op = U32(P.Get(o.File), o.RecOff + (k == 0 ? 4 : 8));
                    string of; int oo;
                    if (op == ptr || !RamMap.ToFile(op, out of, out oo)) continue;
                    byte[] ob = P.Get(of); int e = oo; while (e < ob.Length && ob[e] != 0) e++;
                    if (e - oo != enc.Length) continue;
                    bool eq = true; for (int i = 0; i < enc.Length && eq; i++) if (ob[oo + i] != enc[i]) eq = false;
                    if (!eq) continue;
                    P.Write(it.File, ptrOff, BitConverter.GetBytes((uint)op));
                    if (!sharedNow) ClearOld(f, off, oldEnd, it, isDesc);
                    return null;
                }

            // 2) en su sitio, si cabe hasta el final de su hueco sin pisar otras cadenas
            {
                Span sp = spans.Find(delegate(Span s) { return s.File == f && off >= s.Start && off < s.End; });
                if (sp != null && off + need <= sp.End && Free(occ, off, off + need))
                {
                    byte[] w = new byte[Math.Max(need, oldEnd + 1 - off)];
                    Array.Copy(enc, w, enc.Length);
                    P.Write(f, off, w);
                    return null;
                }
            }

            // 3) reubicar en espacio libre de los huecos de objetos
            int at; string nf = FindFree(need, it, isDesc, out at);
            if (nf == null) return "No cabe (" + enc.Length + " bytes) y no queda espacio libre en los textos de objetos (" + PoolFree() + " bytes libres en total). Acorta el texto.";
            byte[] nw = new byte[need];
            Array.Copy(enc, nw, enc.Length);
            P.Write(nf, at, nw);
            P.Write(it.File, ptrOff, BitConverter.GetBytes((uint)RamMap.ToRam(nf, at)));
            if (!sharedNow) ClearOld(f, off, oldEnd, it, isDesc);
            return null;
        }

        /// <summary>Borra (a 00) la cadena que se abandona, si nadie mas la usa y no tiene punteros ajenos.</summary>
        void ClearOld(string f, int off, int oldEnd, ItemRec it, bool isDesc)
        {
            foreach (Span s in spans) if (s.Pinned && s.File == f && s.Start == off) return;
            List<int[]> occ = Occupied(f, it, isDesc);
            if (!Free(occ, off, oldEnd + 1)) return;
            P.Write(f, off, new byte[oldEnd - off]);
        }

        public void SetPrices(ItemRec it, int buy, int sell)
        {
            byte[] d = new byte[4];
            d[0] = (byte)buy; d[1] = (byte)(buy >> 8); d[2] = (byte)sell; d[3] = (byte)(sell >> 8);
            P.Write(it.File, it.RecOff + 16, d);
        }

        public int NeededBytes(string text)
        {
            try { return P.Codec.Encode(text.Replace("\r\n", "\n")).Length; } catch (Exception) { return -1; }
        }

        public int CurrentSlot(ItemRec it, bool isDesc)
        {
            byte[] ab = Data(it.File);
            int slot; ReadStringAt(U32(ab, it.RecOff + (isDesc ? 8 : 4)), out slot);
            return slot;
        }
    }
}
