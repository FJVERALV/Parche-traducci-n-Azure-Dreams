using System;
using System.IO;
using System.Text;

namespace AzTool
{
    /// <summary>
    /// Parches PPF 3.0 (formato estandar para imagenes de PS1: PPF-O-Matic, MultiPatch, DuckStation...).
    /// Cabecera: "PPF30", metodo 2, descripcion (50 bytes), tipo de imagen (0 = BIN), blockcheck (1),
    /// undo (0), relleno; 1024 bytes del original en 0x9320 (blockcheck: comprueba que el BIN es el correcto);
    /// despues bloques [offset u64][longitud u8][datos].
    /// </summary>
    public static class Ppf
    {
        /// <summary>Crea un PPF con las diferencias entre dos BIN del mismo tamano. Devuelve el numero de bloques.</summary>
        public static long Make(string origPath, string patchedPath, string outPath, string desc)
        {
            byte[] orig = File.ReadAllBytes(origPath);
            byte[] patched = File.ReadAllBytes(patchedPath);
            if (orig.Length != patched.Length) throw new InvalidDataException("Los dos BIN tienen tamaños distintos.");
            using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                byte[] magic = Encoding.ASCII.GetBytes("PPF30");
                fs.Write(magic, 0, magic.Length);
                fs.WriteByte(0x02); // metodo = PPF3.0
                byte[] descBytes = new byte[50];
                byte[] descSrc = Encoding.ASCII.GetBytes(desc.Length > 50 ? desc.Substring(0, 50) : desc);
                Array.Copy(descSrc, descBytes, descSrc.Length);
                for (int i = descSrc.Length; i < 50; i++) descBytes[i] = 0x20;
                fs.Write(descBytes, 0, 50);
                fs.WriteByte(0x00); // imagetype = BIN
                fs.WriteByte(0x01); // blockcheck habilitado
                fs.WriteByte(0x00); // sin datos de deshacer
                fs.WriteByte(0x00); // relleno
                fs.Write(orig, 0x9320, 1024);

                // Cada bloque lleva 9 bytes fijos de cabecera, asi que se fusionan cambios separados por
                // pocos bytes iguales: sale mas barato incluir esos bytes "de mas" que una cabecera nueva.
                const int tol = 9;
                long records = 0;
                int i2 = 0;
                while (i2 < orig.Length)
                {
                    if (orig[i2] == patched[i2]) { i2++; continue; }
                    int start = i2, lastDiff = i2, k = i2 + 1;
                    while (k < orig.Length)
                    {
                        if (orig[k] != patched[k]) { lastDiff = k; k++; continue; }
                        int gapEnd = k;
                        while (gapEnd < orig.Length && orig[gapEnd] == patched[gapEnd] && gapEnd - k < tol) gapEnd++;
                        if (gapEnd < orig.Length && orig[gapEnd] != patched[gapEnd]) { k = gapEnd; continue; }
                        break;
                    }
                    int off = start, remaining = lastDiff - start + 1;
                    i2 = lastDiff + 1;
                    while (remaining > 0)
                    {
                        int chunk = Math.Min(255, remaining);
                        fs.Write(BitConverter.GetBytes((long)off), 0, 8);
                        fs.WriteByte((byte)chunk);
                        fs.Write(patched, off, chunk);
                        records++;
                        off += chunk; remaining -= chunk;
                    }
                }
                return records;
            }
        }

        /// <summary>Aplica el PPF a una copia en memoria del original y la compara byte a byte con el BIN esperado.</summary>
        public static bool Verify(string origPath, string ppfPath, string expectedPath, out string report)
        {
            StringBuilder sb = new StringBuilder();
            byte[] buf = File.ReadAllBytes(origPath);
            byte[] expected = File.ReadAllBytes(expectedPath);
            byte[] ppf = File.ReadAllBytes(ppfPath);
            report = "";
            if (Encoding.ASCII.GetString(ppf, 0, 5) != "PPF30") { report = "No es un PPF 3.0 válido."; return false; }
            int imagetype = ppf[56], blockcheck = ppf[57], undo = ppf[58];
            int p = 60;
            if (blockcheck == 1)
            {
                int bcOff = imagetype == 0 ? 0x9320 : 0x80A0;
                for (int i = 0; i < 1024; i++)
                    if (buf[bcOff + i] != ppf[p + i]) { report = "Blockcheck NO coincide (el BIN original no es la versión correcta) en el byte " + i; return false; }
                sb.AppendLine("Blockcheck OK.");
                p += 1024;
            }
            long records = 0, bytes = 0;
            while (p < ppf.Length)
            {
                long off = BitConverter.ToInt64(ppf, p); p += 8;
                int len = ppf[p]; p += 1;
                for (int i = 0; i < len; i++) buf[off + i] = ppf[p + i];
                p += len;
                if (undo == 1) p += len;
                records++; bytes += len;
            }
            sb.AppendLine("Aplicados " + records + " bloques, " + bytes + " bytes.");
            if (buf.Length != expected.Length) { sb.AppendLine("FALLO: tamaño distinto tras aplicar."); report = sb.ToString(); return false; }
            int diffs = 0; long first = -1;
            for (long i = 0; i < buf.Length; i++) if (buf[i] != expected[i]) { diffs++; if (first < 0) first = i; }
            if (diffs == 0) sb.AppendLine("VERIFICADO: el resultado es idéntico byte a byte al BIN traducido.");
            else sb.AppendLine("FALLO: " + diffs + " bytes distintos. Primero en 0x" + first.ToString("X"));
            report = sb.ToString();
            return diffs == 0;
        }
    }
}
