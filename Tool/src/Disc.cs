using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AzTool
{
    public class IsoEntry
    {
        public string Path;
        public uint Lba;
        public uint Size;
        public bool IsDir;
        public override string ToString() { return Path; }
    }

    /// <summary>Correccion de errores MODE2/2352 Form 1 (EDC + ECC P/Q).</summary>
    public static class Ecc
    {
        static byte[] f = new byte[256];
        static byte[] b = new byte[256];
        static uint[] edc = new uint[256];

        static Ecc()
        {
            for (int i = 0; i < 256; i++)
            {
                int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
                f[i] = (byte)j;
                b[i ^ j] = (byte)i;
                uint e = (uint)i;
                for (int k = 0; k < 8; k++) e = (e >> 1) ^ ((e & 1) != 0 ? 0xD8018001u : 0u);
                edc[i] = e;
            }
        }

        static uint Edc(byte[] s, int off, int len)
        {
            uint c = 0;
            for (int i = 0; i < len; i++) c = (c >> 8) ^ edc[(c ^ s[off + i]) & 0xFF];
            return c;
        }

        static void Block(byte[] src, int srcOff, int majorCount, int minorCount, int majorMult, int minorInc, int destOff)
        {
            int size = majorCount * minorCount;
            for (int major = 0; major < majorCount; major++)
            {
                int index = (major >> 1) * majorMult + (major & 1);
                byte a = 0, bb = 0;
                for (int minor = 0; minor < minorCount; minor++)
                {
                    byte t = src[srcOff + index];
                    index += minorInc;
                    if (index >= size) index -= size;
                    a ^= t; bb ^= t;
                    a = f[a];
                }
                a = b[f[a] ^ bb];
                src[destOff + major] = a;
                src[destOff + major + majorCount] = (byte)(a ^ bb);
            }
        }

        /// <summary>Recalcula EDC y ECC de un sector completo de 2352 bytes (Mode 2 Form 1).</summary>
        public static void Fix(byte[] sec)
        {
            uint e = Edc(sec, 16, 2056);
            sec[2072] = (byte)e; sec[2073] = (byte)(e >> 8); sec[2074] = (byte)(e >> 16); sec[2075] = (byte)(e >> 24);
            byte a0 = sec[12], a1 = sec[13], a2 = sec[14], a3 = sec[15];
            sec[12] = sec[13] = sec[14] = sec[15] = 0;
            Block(sec, 12, 86, 24, 2, 86, 0x81C);
            Block(sec, 12, 52, 43, 86, 88, 0x8C8);
            sec[12] = a0; sec[13] = a1; sec[14] = a2; sec[15] = a3;
        }

        public static bool IsForm2(byte[] sec) { return (sec[18] & 0x20) != 0; }
    }

    public class Disc : IDisposable
    {
        public const int SS = 2352, DATA = 2048, OFF = 24;
        FileStream fs;
        public string Path;
        public long SectorCount;
        List<IsoEntry> files;

        public Disc(string path, bool write)
        {
            Path = path;
            fs = new FileStream(path, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read, 1 << 16);
            if (fs.Length % SS != 0)
                throw new InvalidDataException("El archivo no parece un BIN de 2352 bytes/sector (MODE2/2352).");
            SectorCount = fs.Length / SS;
            byte[] s = ReadRaw(16);
            if (!(s[0] == 0 && s[1] == 0xFF && s[11] == 0))
                throw new InvalidDataException("Sector sin cabecera de sincronia: no es un BIN MODE2/2352.");
        }

        public void Dispose() { if (fs != null) { fs.Dispose(); fs = null; } }

        public byte[] ReadRaw(long lba)
        {
            byte[] b = new byte[SS];
            fs.Seek(lba * SS, SeekOrigin.Begin);
            int n = 0;
            while (n < SS) { int r = fs.Read(b, n, SS - n); if (r <= 0) break; n += r; }
            return b;
        }

        public byte[] ReadData(long lba)
        {
            byte[] raw = ReadRaw(lba);
            byte[] d = new byte[DATA];
            Buffer.BlockCopy(raw, OFF, d, 0, DATA);
            return d;
        }

        public void WriteData(long lba, byte[] data)
        {
            byte[] raw = ReadRaw(lba);
            if (Ecc.IsForm2(raw)) throw new InvalidOperationException("Sector " + lba + " es Form 2 (video/audio); no se puede escribir.");
            Buffer.BlockCopy(data, 0, raw, OFF, DATA);
            Ecc.Fix(raw);
            fs.Seek(lba * SS, SeekOrigin.Begin);
            fs.Write(raw, 0, SS);
        }

        /// <summary>Comprueba que el EDC/ECC calculado coincide con el original en el sector dado.</summary>
        public bool VerifyEcc(long lba)
        {
            byte[] raw = ReadRaw(lba);
            if (Ecc.IsForm2(raw)) return true;
            byte[] copy = (byte[])raw.Clone();
            Ecc.Fix(copy);
            for (int i = 0; i < SS; i++) if (copy[i] != raw[i]) return false;
            return true;
        }

        public byte[] ReadFile(IsoEntry e)
        {
            byte[] r = new byte[e.Size];
            int sectors = (int)((e.Size + DATA - 1) / DATA);
            for (int i = 0; i < sectors; i++)
            {
                byte[] d = ReadData(e.Lba + i);
                int n = (int)Math.Min(DATA, e.Size - (long)i * DATA);
                Buffer.BlockCopy(d, 0, r, i * DATA, n);
            }
            return r;
        }

        /// <summary>Escribe bytes dentro de un archivo del ISO (sin cambiar su tamano).</summary>
        public void PatchFile(IsoEntry e, long offset, byte[] bytes)
        {
            if (offset < 0 || offset + bytes.Length > e.Size)
                throw new ArgumentException("Parche fuera del archivo " + e.Path);
            long pos = offset;
            int done = 0;
            while (done < bytes.Length)
            {
                long lba = e.Lba + pos / DATA;
                int inSec = (int)(pos % DATA);
                int n = Math.Min(DATA - inSec, bytes.Length - done);
                byte[] d = ReadData(lba);
                Buffer.BlockCopy(bytes, done, d, inSec, n);
                WriteData(lba, d);
                pos += n; done += n;
            }
        }

        public void ReplaceFile(IsoEntry e, byte[] data)
        {
            if (data.Length > e.Size) throw new ArgumentException("El archivo nuevo (" + data.Length + ") es mayor que el original (" + e.Size + ").");
            PatchFile(e, 0, data);
        }

        public List<IsoEntry> ListFiles()
        {
            if (files != null) return files;
            files = new List<IsoEntry>();
            byte[] pvd = ReadData(16);
            uint rootLba = BitConverter.ToUInt32(pvd, 158);
            uint rootLen = BitConverter.ToUInt32(pvd, 166);
            Walk(rootLba, rootLen, "");
            return files;
        }

        void Walk(uint lba, uint len, string prefix)
        {
            byte[] data = new byte[((len + DATA - 1) / DATA) * DATA];
            for (int i = 0; i < data.Length / DATA; i++)
                Buffer.BlockCopy(ReadData(lba + i), 0, data, i * DATA, DATA);
            int p = 0;
            while (p < len)
            {
                int l = data[p];
                if (l == 0) { p = (p / DATA + 1) * DATA; continue; }
                uint el = BitConverter.ToUInt32(data, p + 2);
                uint sz = BitConverter.ToUInt32(data, p + 10);
                bool dir = (data[p + 25] & 2) != 0;
                int nl = data[p + 32];
                if (nl > 1 || data[p + 33] > 1)
                {
                    string name = Encoding.ASCII.GetString(data, p + 33, nl);
                    int semi = name.IndexOf(';');
                    if (semi >= 0) name = name.Substring(0, semi);
                    IsoEntry e = new IsoEntry { Path = prefix + name, Lba = el, Size = sz, IsDir = dir };
                    files.Add(e);
                    if (dir) Walk(el, sz, prefix + name + "/");
                }
                p += l;
            }
        }
    }
}
