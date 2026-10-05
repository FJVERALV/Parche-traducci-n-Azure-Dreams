using System;
using System.Collections.Generic;
using System.Drawing;

namespace AzTool
{
    /// <summary>
    /// Descompresor de graficos de Azure Dreams (recursos tipo 1 del directorio en MAIN/DUNGEON:
    /// iconos, texturas de menu, fuente). Reimplementacion propia (clean-room) del algoritmo LZ
    /// identificado en el proyecto de decompilacion de la comunidad (funcion que el codigo fuente
    /// original llama junto a LoadImage() para subir texturas a VRAM): flags de 8 bits, bit=0 es un
    /// literal, bit=1 introduce una copia (larga de 2 bytes o corta de un byte), terminando cuando
    /// la copia larga trae un desplazamiento de 0. Verificado contra el disco: en MAIN.BIN,
    /// offset 0x271D7C descomprime a exactamente 8192 bytes (=32*128*2, el tamano calculado para
    /// un recurso 128x128 4bpp), produciendo una imagen con contenido real (no ruido).
    /// </summary>
    public static class GfxCodec
    {
        /// <summary>Intenta descomprimir desde start. Devuelve null si el flujo no es valido
        /// (desplazamiento fuera de rango, o se acaba el archivo antes de la marca de fin).</summary>
        public static byte[] TryDecode(byte[] src, int start, int maxOut, out int consumed)
        {
            consumed = 0;
            if (start < 0 || start >= src.Length) return null;
            int p = start;
            byte[] dst = new byte[maxOut];
            int n = 0;
            int flags = 0, bitsLeft = 0;
            int len = src.Length;
            while (true)
            {
                if (bitsLeft == 0) { if (p >= len) return null; flags = src[p]; p++; bitsLeft = 8; }
                int bit = flags & 1; flags >>= 1; bitsLeft--;
                if (bit == 0)
                {
                    if (n >= maxOut || p >= len) return null;
                    dst[n++] = src[p]; p++;
                    continue;
                }
                if (bitsLeft == 0) { if (p >= len) return null; flags = src[p]; p++; bitsLeft = 8; }
                int bit2 = flags & 1; flags >>= 1; bitsLeft--;
                int count, offset;
                if (bit2 == 0)
                {
                    if (p + 1 >= len) return null;
                    int offsetWord = (src[p] << 8) | src[p + 1]; p += 2;
                    if (offsetWord == 0)
                    {
                        consumed = p - start;
                        byte[] outp = new byte[n]; Array.Copy(dst, outp, n);
                        return outp;
                    }
                    count = offsetWord & 0xF;
                    if (count != 0) count += 2; else { if (p >= len) return null; count = src[p] + 1; p++; }
                    offset = offsetWord >> 4;
                }
                else
                {
                    if (bitsLeft == 0) { if (p >= len) return null; flags = src[p]; p++; bitsLeft = 8; }
                    int c0 = flags & 1; flags >>= 1; bitsLeft--;
                    if (bitsLeft == 0) { if (p >= len) return null; flags = src[p]; p++; bitsLeft = 8; }
                    int c1 = flags & 1; flags >>= 1; bitsLeft--;
                    count = ((c0 << 1) | c1) + 2;
                    if (p >= len) return null;
                    offset = src[p]; p++;
                    if (offset == 0) offset = 0x100;
                }
                if (offset <= 0 || offset > n || n + count > maxOut) return null;
                for (int i = 0; i < count; i++) { dst[n] = dst[n - offset]; n++; }
            }
        }

        // ---- codificador (inverso del descompresor) -------------------------------------------
        // Costes en bits: literal 1+8=9; copia corta (largo 2..5, dist 1..256) 4+8=12;
        // copia larga (largo 3..17, dist 1..4095) 2+16=18; larga con byte extra (largo 18..256) 2+24=26.

        class BitWriter
        {
            public List<byte> Out = new List<byte>();
            int flagPos = -1, used = 8;
            public void Bit(int b)
            {
                if (used == 8) { flagPos = Out.Count; Out.Add(0); used = 0; }
                if (b != 0) Out[flagPos] |= (byte)(1 << used);
                used++;
            }
            public void Byte(int v) { Out.Add((byte)v); }
        }

        /// <summary>Comprime con parsing optimo (programacion dinamica). El resultado se descomprime
        /// con TryDecode/func del juego y termina con la marca de fin (copia larga con desplazamiento 0).</summary>
        public static byte[] Encode(byte[] data)
        {
            int n = data.Length;
            int[] cost = new int[n + 1];
            int[] choiceLen = new int[n + 1];   // 0 = literal
            int[] choiceDist = new int[n + 1];
            cost[n] = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                int best = 9 + cost[i + 1]; int bl = 0, bd = 0;
                int maxShort = 0, maxLong = 0, distShort = 0, distLong = 0;
                int maxD = Math.Min(i, 4095);
                for (int d = 1; d <= maxD; d++)
                {
                    int l = 0;
                    while (l < 256 && i + l < n && data[i + l] == data[i + l - d]) l++;
                    if (l > maxLong) { maxLong = l; distLong = d; }
                    if (d <= 256 && l > maxShort) { maxShort = l; distShort = d; }
                }
                for (int l = 2; l <= Math.Min(5, maxShort); l++)
                {
                    int c = 12 + cost[i + l];
                    if (c < best) { best = c; bl = l; bd = distShort; }
                }
                for (int l = 3; l <= Math.Min(17, maxLong); l++)
                {
                    int c = 18 + cost[i + l];
                    if (c < best) { best = c; bl = l; bd = distLong; }
                }
                for (int l = 18; l <= maxLong; l++)
                {
                    int c = 26 + cost[i + l];
                    if (c < best) { best = c; bl = l; bd = distLong; }
                }
                cost[i] = best; choiceLen[i] = bl; choiceDist[i] = bd;
            }
            BitWriter w = new BitWriter();
            int p = 0;
            while (p < n)
            {
                int l = choiceLen[p], d = choiceDist[p];
                if (l == 0) { w.Bit(0); w.Byte(data[p]); p++; continue; }
                if (l <= 5 && d <= 256 && Cheaper(l, d)) { w.Bit(1); w.Bit(1); int c = l - 2; w.Bit((c >> 1) & 1); w.Bit(c & 1); w.Byte(d == 256 ? 0 : d); p += l; continue; }
                w.Bit(1); w.Bit(0);
                if (l <= 17) { int word = (d << 4) | (l - 2); w.Byte(word >> 8); w.Byte(word & 0xFF); }
                else { int word = d << 4; w.Byte(word >> 8); w.Byte(word & 0xFF); w.Byte(l - 1); }
                p += l;
            }
            w.Bit(1); w.Bit(0); w.Byte(0); w.Byte(0);
            return w.Out.ToArray();
        }
        static bool Cheaper(int l, int d) { return true; }

        public class Hit { public string File; public int Offset; public int Consumed; public int OutLen; public double Ratio; public Bitmap Thumb; }

        /// <summary>Escanea todo el archivo probando cada posicion de byte; se queda con los
        /// resultados con buena razon de compresion (probablemente datos reales, no ruido).</summary>
        public static List<Hit> Scan(byte[] src, int minOut, int maxOut, double minRatio, int maxOutBuf)
        {
            List<Hit> hits = new List<Hit>();
            for (int off = 0; off < src.Length; off++)
            {
                int consumed;
                byte[] r = TryDecode(src, off, maxOutBuf, out consumed);
                if (r == null || r.Length < minOut || r.Length > maxOut) continue;
                double ratio = (double)r.Length / Math.Max(1, consumed);
                if (ratio >= minRatio) hits.Add(new Hit { Offset = off, Consumed = consumed, OutLen = r.Length, Ratio = ratio });
            }
            return hits;
        }
    }
}
