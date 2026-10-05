using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AzTool
{
    /// <summary>
    /// Mensajes de combate, objetos y estados de la torre (DUNGEON.BIN), guardados en un formato comprimido propio:
    ///   tabla de 54 letras SJIS (las mas frecuentes del ingles) en 0xFAC8C;
    ///   cada mensaje: [0x51 letras... 0x00] = tramo comprimido (byte n = letra n de la tabla),
    ///   fuera de tramo: 0x0A salto de linea, pares SJIS literales, 0x00 fin de mensaje.
    /// El codigo apunta a cada mensaje por su direccion (lui/addiu), asi que cada traduccion se escribe
    /// en el mismo sitio y con la misma longitud que el original; el sobrante se rellena con tramos vacios (51 00).
    /// </summary>
    public static class BattleMsgs
    {
        public const string File = "DUNGEON/DUNGEON.BIN";
        const int TableOff = 0xFAC8C, TableLen = 54, BlockStart = 0xFAD02, BlockEnd = 0xFC9B6;

        public class Msg { public int Index, Offset, Length; public byte[] Prefix; public string Text; }

        static Dictionary<ushort, byte> Table(byte[] d)
        {
            Dictionary<ushort, byte> t = new Dictionary<ushort, byte>();
            for (int i = 0; i < TableLen; i++) t[(ushort)((d[TableOff + 2 * i] << 8) | d[TableOff + 2 * i + 1])] = (byte)(i + 1);
            return t;
        }

        static bool Lead(byte b) { return (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xEF); }

        /// <summary>Lee los mensajes del bloque del archivo original.</summary>
        public static List<Msg> Parse(byte[] d, SjisCodec codec)
        {
            List<Msg> list = new List<Msg>();
            int p = BlockStart;
            while (p < BlockEnd)
            {
                Msg m = new Msg { Index = list.Count, Offset = p };
                List<byte> pre = new List<byte>();
                while (d[p] != 0x51 && d[p] != 0 && d[p] != 0x0A && !Lead(d[p])) pre.Add(d[p++]);   // bytes previos (p.ej. 30 30)
                m.Prefix = pre.ToArray();
                List<byte> sj = new List<byte>();
                while (true)
                {
                    byte b = d[p];
                    if (b == 0x51)
                    {
                        p++;
                        while (d[p] != 0) { int c = d[p++] - 1; sj.Add(d[TableOff + 2 * c]); sj.Add(d[TableOff + 2 * c + 1]); }
                        p++; continue;
                    }
                    if (b == 0) { p++; break; }
                    if (Lead(b)) { sj.Add(b); sj.Add(d[p + 1]); p += 2; continue; }
                    sj.Add(b); p++;
                }
                m.Length = p - m.Offset;
                m.Text = codec.Decode(sj.ToArray(), 0, sj.Count);
                list.Add(m);
            }
            return list;
        }

        static string FoldAccents(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '¿' || c == '¡') continue;
                if (c == 'ñ') { sb.Append('n'); continue; }
                if (c == 'Ñ') { sb.Append('N'); continue; }
                foreach (char k in c.ToString().Normalize(NormalizationForm.FormD))
                    if (CharUnicodeInfo.GetUnicodeCategory(k) != UnicodeCategory.NonSpacingMark) sb.Append(k);
            }
            return sb.ToString();
        }

        // Codifica sin relleno: lista de "piezas" (tramo = lista de indices, o bytes sueltos).
        static List<object> Tokens(byte[] sj, Dictionary<ushort, byte> tab, bool runs)
        {
            List<object> toks = new List<object>();
            List<byte> run = null;
            for (int i = 0; i < sj.Length; )
            {
                byte b = sj[i];
                if (Lead(b) && i + 1 < sj.Length)
                {
                    ushort code = (ushort)((b << 8) | sj[i + 1]);
                    byte idx;
                    if (runs && tab.TryGetValue(code, out idx)) { if (run == null) { run = new List<byte>(); toks.Add(run); } run.Add(idx); }
                    else { run = null; toks.Add(new byte[] { b, sj[i + 1] }); }
                    i += 2; continue;
                }
                run = null; toks.Add(new byte[] { b }); i++;
            }
            return toks;
        }

        static int Size(List<object> toks)
        {
            int n = 1;   // fin de mensaje
            foreach (object t in toks) n += t is List<byte> ? ((List<byte>)t).Count + 2 : ((byte[])t).Length;
            return n;
        }

        /// <summary>Cambia en 1 byte el tamano (para que el relleno de 2 en 2 cuadre):
        /// la ultima letra de un tramo pasa a SJIS literal, o un literal que esta en la tabla pasa a tramo.</summary>
        static bool FlipParity(List<object> toks, Dictionary<ushort, byte> tab, byte[] file)
        {
            int k = toks.FindLastIndex(delegate(object x) { return x is List<byte>; });
            if (k >= 0)
            {
                List<byte> r = (List<byte>)toks[k];
                byte idx = r[r.Count - 1]; r.RemoveAt(r.Count - 1);
                byte[] lit = { file[TableOff + 2 * (idx - 1)], file[TableOff + 2 * (idx - 1) + 1] };
                if (r.Count == 0) toks[k] = lit; else toks.Insert(k + 1, lit);
                return true;
            }
            for (int i = toks.Count - 1; i >= 0; i--)
            {
                byte[] b = toks[i] as byte[]; byte idx;
                if (b != null && b.Length == 2 && tab.TryGetValue((ushort)((b[0] << 8) | b[1]), out idx)) { toks[i] = new List<byte> { idx }; return true; }
            }
            return false;
        }

        /// <summary>Codifica un texto para un hueco de exactamente 'len' bytes (incluidos prefijo y fin). Devuelve null si no cabe.</summary>
        public static byte[] Encode(SjisCodec codec, byte[] file, string text, byte[] prefix, int len)
        {
            Dictionary<ushort, byte> tab = Table(file);
            string fold = FoldAccents(text);
            // por orden de preferencia: con tildes y comprimido, sin tildes y comprimido, y luego sin comprimir
            object[][] cands = { new object[] { text, true }, new object[] { fold, true }, new object[] { text, false }, new object[] { fold, false } };
            foreach (object[] cand in cands)
            {
                List<object> toks = Tokens(codec.Encode((string)cand[0]), tab, (bool)cand[1]);
                int pad = len - prefix.Length - Size(toks);
                if (pad < 0) continue;
                if ((pad & 1) == 1 && !FlipParity(toks, tab, file)) continue;
                pad = len - prefix.Length - Size(toks);
                if (pad < 0 || (pad & 1) == 1) continue;
                List<byte> o = new List<byte>(prefix);
                foreach (object t in toks)
                {
                    if (t is List<byte>) { o.Add(0x51); o.AddRange((List<byte>)t); o.Add(0x00); }
                    else o.AddRange((byte[])t);
                }
                for (int i = 0; i < pad; i += 2) { o.Add(0x51); o.Add(0x00); }   // tramos vacios: no dibujan nada
                o.Add(0x00);
                return o.ToArray();
            }
            return null;
        }

        /// <summary>Bytes minimos que ocupa un texto (prefijo y fin incluidos), o -1 si tiene caracteres no validos.</summary>
        public static int NeededBytes(SjisCodec codec, byte[] file, string text, byte[] prefix)
        {
            Dictionary<ushort, byte> tab = Table(file);
            int best = -1;
            foreach (string t in new string[] { text, FoldAccents(text) })
            {
                try { int n = prefix.Length + Size(Tokens(codec.Encode(t), tab, true)); if (best < 0 || n < best) best = n; }
                catch (Exception) { }
            }
            return best;
        }

        /// <summary>Aplica mensajes_es.csv (id;offset;max_bytes;original;traduccion) al proyecto.</summary>
        public static string Apply(Project p, string csv)
        {
            if (!System.IO.File.Exists(csv)) return "Mensajes de combate: no hay " + Path.GetFileName(csv);
            byte[] orig = p.GetOriginal(File);
            List<Msg> msgs = Parse(orig, p.Codec);
            List<string[]> rows = Csv.Read(csv); string[] h = rows[0];
            int cid = Csv.Col(h, "id"), ct = Csv.Col(h, "traduccion");
            int ok = 0; StringBuilder bad = new StringBuilder();
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i]; if (r.Length <= ct || r[ct].Length == 0) continue;
                int id = int.Parse(r[cid]); if (id < 0 || id >= msgs.Count) continue;
                Msg m = msgs[id];
                string txt = r[ct] == "∅" ? "" : r[ct];   // ∅ = mensaje vacio a proposito
                byte[] enc = Encode(p.Codec, orig, txt, m.Prefix, m.Length);
                if (enc == null) { bad.Append(" " + id); continue; }
                p.Write(File, m.Offset, enc); ok++;
            }
            return "Mensajes de combate: aplicados=" + ok + (bad.Length > 0 ? " NO CABEN:" + bad : "");
        }

        /// <summary>Exporta los mensajes originales a CSV para traducir.</summary>
        public static void Export(Project p, string csv)
        {
            List<Msg> msgs = Parse(p.GetOriginal(File), p.Codec);
            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "id", "offset", "max_bytes", "original", "traduccion" });
            foreach (Msg m in msgs) rows.Add(new string[] { m.Index.ToString(), m.Offset.ToString("X"), m.Length.ToString(), m.Text, "" });
            Csv.Write(csv, rows, ';');
        }
    }
}
