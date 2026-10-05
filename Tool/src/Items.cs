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
        // zona libre verificada en el SLUS (RAM 0x8007BCB0-0x8007BEF0)
        public const int PoolStart = 0x4F4B0, PoolEnd = 0x4F6F0;

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

        /// <summary>Primera posicion libre del hueco: tras el ultimo byte usado, su 00 terminador y alineada a 4.
        /// (Antes se alineaba sin contar el 00: un nombre de 4, 8, 12... bytes perdia su terminador y el juego
        /// lo leia pegado al siguiente, p.ej. "RojaBlancaCajaCarne", y desbordaba la lista de la tienda.)</summary>
        int PoolNext()
        {
            byte[] s = P.Get(RamMap.Slus);
            int last = PoolStart;
            for (int i = PoolStart; i < PoolEnd; i++) if (s[i] != 0) last = i + 2;   // +1 del byte, +1 de su 00
            return (last + 3) & ~3;
        }

        public int PoolFree() { return Math.Max(0, PoolEnd - PoolNext()); }

        int PoolAlloc(int need)
        {
            int at = PoolNext();
            if (at + need > PoolEnd) return -1;
            return at;
        }

        /// <summary>Escribe un texto (nombre o descripcion). Si no cabe en su hueco lo reubica y corrige el puntero.</summary>
        public string SetString(ItemRec it, bool isDesc, string text)
        {
            byte[] ab = P.Get(it.File);
            int ptrOff = it.RecOff + (isDesc ? 8 : 4);
            long ptr = U32(ab, ptrOff);
            string f; int off;
            if (!RamMap.ToFile(ptr, out f, out off)) return "Puntero invalido";
            byte[] enc;
            try { enc = P.Codec.Encode(text.Replace("\r\n", "\n")); }
            catch (Exception ex) { return ex.Message; }
            int slot; ReadStringAt(ptr, out slot);
            if (enc.Length <= slot)
            {
                byte[] w = new byte[slot + 1];
                Array.Copy(enc, w, enc.Length);
                P.Write(f, off, w);
                return null;
            }
            int at = PoolAlloc(enc.Length + 1);
            if (at < 0) return "No cabe (" + enc.Length + " bytes, hueco " + slot + ") y no queda espacio libre para reubicar (" + PoolFree() + " bytes).";
            byte[] nw = new byte[enc.Length + 1];
            Array.Copy(enc, nw, enc.Length);
            P.Write(RamMap.Slus, at, nw);
            long newRam = RamMap.SlusBase + at;
            P.Write(it.File, ptrOff, BitConverter.GetBytes((uint)newRam));
            return null;
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
