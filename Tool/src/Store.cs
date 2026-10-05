using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AzTool
{
    public class Patch
    {
        public string File;
        public int Offset;
        public byte[] Data;
    }

    /// <summary>
    /// Estado del proyecto: el disco original (solo lectura) y una copia editable en memoria de
    /// cada archivo tocado. TODOS los editores escriben a traves de Write().
    /// </summary>
    public class Project
    {
        public Disc Disc;
        public string BinPath;
        public SjisCodec Codec = new SjisCodec();
        public event EventHandler Changed;

        Dictionary<string, byte[]> cur = new Dictionary<string, byte[]>();
        Dictionary<string, byte[]> orig = new Dictionary<string, byte[]>();
        HashSet<string> dirty = new HashSet<string>();

        public IsoEntry Find(string path)
        {
            return Disc.ListFiles().Find(delegate(IsoEntry e) { return e.Path == path; });
        }

        public byte[] Get(string path)
        {
            byte[] b;
            if (cur.TryGetValue(path, out b)) return b;
            IsoEntry e = Find(path);
            if (e == null) throw new FileNotFoundException(path);
            byte[] data = Disc.ReadFile(e);
            orig[path] = data;
            b = (byte[])data.Clone();
            cur[path] = b;
            return b;
        }

        public byte[] GetOriginal(string path) { Get(path); return orig[path]; }

        /// <summary>Offset "crudo" del .bin (2352 bytes/sector, como los de adrando) a (archivo, offset).</summary>
        public bool RawToFile(long raw, out string file, out int off)
        {
            file = null; off = 0;
            long lba = raw / 2352; int inSec = (int)(raw % 2352) - 24;
            if (inSec < 0 || inSec >= 2048) return false;
            foreach (IsoEntry e in Disc.ListFiles())
            {
                if (e.IsDir) continue;
                long secs = (e.Size + 2047) / 2048;
                if (lba >= e.Lba && lba < e.Lba + secs) { file = e.Path; off = (int)((lba - e.Lba) * 2048 + inSec); return true; }
            }
            return false;
        }

        public void Write(string path, int offset, byte[] data)
        {
            byte[] b = Get(path);
            if (offset < 0 || offset + data.Length > b.Length) throw new ArgumentException("Escritura fuera de " + path);
            bool diff = false;
            for (int i = 0; i < data.Length; i++) if (b[offset + i] != data[i]) { diff = true; break; }
            if (!diff) return;
            Buffer.BlockCopy(data, 0, b, offset, data.Length);
            dirty.Add(path);
            Notify();
        }

        /// <summary>Para editores que modifican el buffer devuelto por Get() directamente.</summary>
        public void ForceDirty(string path) { dirty.Add(path); Notify(); }

        public void Notify() { if (Changed != null) Changed(this, EventArgs.Empty); }

        public bool IsDirty(string path) { return dirty.Contains(path); }
        public int DirtyCount { get { return dirty.Count; } }

        public void ReplaceFile(string path, byte[] data)
        {
            byte[] b = Get(path);
            if (data.Length > b.Length) throw new ArgumentException("El archivo nuevo es mayor (" + data.Length + " > " + b.Length + " bytes).");
            byte[] full = (byte[])b.Clone();
            Buffer.BlockCopy(data, 0, full, 0, data.Length);
            Write(path, 0, full);
        }

        public void RevertFile(string path)
        {
            if (!cur.ContainsKey(path)) return;
            cur[path] = (byte[])orig[path].Clone();
            dirty.Remove(path);
            Notify();
        }

        /// <summary>Lista de diferencias respecto al disco original (fusiona huecos pequenos).</summary>
        public List<Patch> Diff()
        {
            List<Patch> res = new List<Patch>();
            foreach (string path in dirty)
            {
                byte[] c = cur[path], o = orig[path];
                int i = 0;
                while (i < c.Length)
                {
                    if (c[i] == o[i]) { i++; continue; }
                    int s = i, lastDiff = i;
                    while (i < c.Length && i - lastDiff <= 16) { if (c[i] != o[i]) lastDiff = i; i++; }
                    int len = lastDiff - s + 1;
                    byte[] d = new byte[len];
                    Buffer.BlockCopy(c, s, d, 0, len);
                    res.Add(new Patch { File = path, Offset = s, Data = d });
                    i = lastDiff + 1;
                }
            }
            return res;
        }

        public void SavePatches(string file)
        {
            using (StreamWriter w = new StreamWriter(file, false, new UTF8Encoding(false)))
            {
                w.WriteLine("#azpatch v1");
                foreach (Patch p in Diff())
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in p.Data) sb.Append(b.ToString("x2"));
                    w.WriteLine(p.File + "\t" + p.Offset.ToString("X") + "\t" + sb);
                }
            }
        }

        public int LoadPatches(string file)
        {
            int n = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p.Length < 3) continue;
                byte[] d = new byte[p[2].Length / 2];
                for (int i = 0; i < d.Length; i++) d[i] = Convert.ToByte(p[2].Substring(i * 2, 2), 16);
                Write(p[0], Convert.ToInt32(p[1], 16), d);
                n++;
            }
            return n;
        }

        /// <summary>Crea una imagen nueva copiando el original y aplicando todos los cambios (recalcula EDC/ECC).</summary>
        public int Build(string outPath)
        {
            File.Copy(BinPath, outPath, true);
            int runs = 0;
            using (Disc o = new Disc(outPath, true))
            {
                foreach (Patch p in Diff())
                {
                    IsoEntry e = o.ListFiles().Find(delegate(IsoEntry x) { return x.Path == p.File; });
                    o.PatchFile(e, p.Offset, p.Data);
                    runs++;
                }
            }
            string cue = Path.ChangeExtension(outPath, ".cue");
            File.WriteAllText(cue, "FILE \"" + Path.GetFileName(outPath) + "\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
            return runs;
        }
    }

    /// <summary>Traduccion de direcciones RAM de PSX a (archivo, offset) para los bloques residentes conocidos.</summary>
    public static class RamMap
    {
        public const string Slus = "SLUS_006.14";
        public const string Main = "MAIN/MAIN.BIN";
        public const long SlusBase = 0x8002C800;  // RAM = SlusBase + offset_en_SLUS (cabecera 0x800 incluida)
        public const long SlusEnd = 0x80081800;
        public const long MainBase = 0x8000B800;  // RAM = MainBase + offset_en_MAIN

        public static bool ToFile(long ram, out string file, out int off)
        {
            file = null; off = 0;
            if (ram >= 0x8002D000L && ram < SlusEnd) { file = Slus; off = (int)(ram - SlusBase); return true; }
            if (ram >= MainBase && ram < 0x8002D000L) { file = Main; off = (int)(ram - MainBase); return true; }
            return false;
        }

        public static long ToRam(string file, int off)
        {
            if (file == Slus) return SlusBase + off;
            if (file == Main) return MainBase + off;
            return 0;
        }
    }
}
