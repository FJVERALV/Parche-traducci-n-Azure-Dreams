using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AzTool
{
    public class TextEntry
    {
        public int Id;
        public string File;      // ruta dentro del ISO, p.ej. MAIN/MAIN.BIN
        public long Offset;      // offset dentro del archivo
        public int Length;       // bytes originales (incluye los codigos de control)
        public int CtlBytes;     // de esos bytes, cuantos son codigos de control (salto de linea, pagina, nombre del heroe...)
        public bool EndsDialog;  // justo despues viene el byte 00 (o 16 = return): fin del dialogo
        public byte[] OrigBytes;
        public string Original;  // texto legible, con etiquetas {HEROE} {PAG} {VENT} {xx}
        public string Translation = "";
        public int UsedBytes;    // bytes que ocupa la traduccion (0 si vacia, -1 si no valida)
        public int UsedCtl;      // bytes de control dentro de la traduccion
        public bool IsJapanese;  // texto original japones (kana) que sigue en el disco
        public string Warn = ""; // avisos de maquetacion (lineas largas, ventanas de mas de 3 lineas)

        /// <summary>Caracteres de texto disponibles (2 bytes cada uno, sin contar codigos de control).</summary>
        public int MaxChars { get { return (Length - CtlBytes) / 2; } }
        /// <summary>Caracteres de texto usados por la traduccion.</summary>
        public int UsedChars { get { return UsedBytes <= 0 ? 0 : (UsedBytes - UsedCtl + 1) / 2; } }
        public bool Over { get { return UsedBytes > Length; } }
        public bool Bad { get { return UsedBytes < 0 || UsedBytes > Length; } }
    }

    /// <summary>Conversion entre el texto del juego (Shift-JIS ancho completo + codigos de control) y texto normal con etiquetas.</summary>
    public class SjisCodec
    {
        static Encoding strict = Encoding.GetEncoding(932, new EncoderExceptionFallback(), new DecoderExceptionFallback());
        Dictionary<char, ushort> extraEnc = new Dictionary<char, ushort>();
        Dictionary<ushort, char> extraDec = new Dictionary<ushort, char>();

        public const string TagHero = "{HEROE}", TagHero2 = "{HEROE2}", TagPage = "{PAG}", TagWin = "{VENT}";
        static Regex tagRx = new Regex(@"\{(HEROE2|HEROE|PAG|VENT|[0-9A-Fa-f]{2})\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        Dictionary<ushort, int> extraCell = new Dictionary<ushort, int>();
        public string CharmapPath;

        /// <summary>Carga charmap.txt: lineas "814F=á@113" (codigo SJIS en hex = caracter, @ celda de la fuente
        /// donde esta dibujado; la celda es opcional para las letras del espanol, que ya tienen una por defecto). '#' comenta.</summary>
        public void LoadCharmap(string path)
        {
            extraEnc.Clear(); extraDec.Clear(); extraCell.Clear();
            CharmapPath = path;
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq != 4 || line.Length < 6) continue;
                ushort code = Convert.ToUInt16(line.Substring(0, 4), 16);
                char ch = line[5];
                extraEnc[ch] = code; extraDec[code] = ch;
                int at = line.IndexOf('@', 6), cell;
                if (at > 0 && int.TryParse(line.Substring(at + 1).Trim(), out cell)) extraCell[code] = cell;
            }
        }

        /// <summary>Entradas del charmap: codigo SJIS, caracter y celda (-1 si no se indico).</summary>
        public List<KeyValuePair<ushort, KeyValuePair<char, int>>> Charmap()
        {
            List<KeyValuePair<ushort, KeyValuePair<char, int>>> l = new List<KeyValuePair<ushort, KeyValuePair<char, int>>>();
            foreach (KeyValuePair<ushort, char> kv in extraDec)
            {
                int c; if (!extraCell.TryGetValue(kv.Key, out c)) c = -1;
                l.Add(new KeyValuePair<ushort, KeyValuePair<char, int>>(kv.Key, new KeyValuePair<char, int>(kv.Value, c)));
            }
            return l;
        }

        /// <summary>Sustituye el charmap en memoria y lo guarda en path (formato CODIGO=c@celda).</summary>
        public void SaveCharmap(string path, List<KeyValuePair<ushort, KeyValuePair<char, int>>> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Letras nuevas del juego (ver DOCUMENTACION.md, apartado Fuente).\r\n");
            sb.Append("# Cada linea: CODIGO_SJIS=caracter@celda\r\n");
            sb.Append("#   CODIGO_SJIS: simbolo de la tabla de caracteres del ejecutable que se reutiliza (ningun texto lo usa).\r\n");
            sb.Append("#   caracter:    letra que escribes en la traduccion.\r\n");
            sb.Append("#   celda:       celda de la fuente (0-127) donde esta dibujada la letra.\r\n");
            foreach (KeyValuePair<ushort, KeyValuePair<char, int>> e in entries)
                sb.Append(e.Key.ToString("X4")).Append('=').Append(e.Value.Key).Append(e.Value.Value >= 0 ? "@" + e.Value.Value : "").Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            LoadCharmap(path);
        }

        /// <summary>Si es true, los caracteres que la fuente del juego no tiene (acentos, enie...) se convierten al equivalente sin tilde.</summary>
        public bool StripAccents = true;

        static string Fold(char c)
        {
            switch (c)
            {
                case '\u00F1': return "n"; case '\u00D1': return "N";
                case '\u00BF': case '\u00A1': return "";
                case '\u201C': case '\u201D': case '\u00AB': case '\u00BB': return "\"";
                case '\u2018': case '\u2019': return "'";
                case '\u2013': case '\u2014': return "-";
                case '\u2026': return "...";
                case '\u00BA': case '\u00AA': return "";
            }
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char k in d)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(k) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(k);
            string r = sb.ToString();
            return (r.Length > 0 && r[0] < 128) ? r : c.ToString();
        }

        /// <summary>Texto tal y como acabara en el juego (sin tildes si StripAccents esta activo).</summary>
        public string Simplify(string s)
        {
            if (!StripAccents) return s;
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c < 128 || extraEnc.ContainsKey(c)) sb.Append(c);
                else sb.Append(Fold(c));
            }
            return sb.ToString();
        }

        public bool HasExtra { get { return extraEnc.Count > 0; } }
        public IEnumerable<char> ExtraChars() { return extraEnc.Keys; }

        static char ToNormal(char c)
        {
            if (c == '\uFF5B' || c == '\uFF5D') return c;          // llaves anchas: no confundir con etiquetas
            if (c == '\u3000') return ' ';
            if (c >= '\uFF01' && c <= '\uFF5E') return (char)(c - 0xFEE0);
            if (c == '\u2019') return '\'';
            if (c == '\u2018') return '`';
            if (c == '\u201D' || c == '\u201C') return '"';
            return c;
        }

        static char ToWide(char c)
        {
            if (c == ' ') return '\u3000';
            if (c == '\'') return '\u2019';
            if (c == '"') return '\u201D';
            if (c == '`') return '\u2018';
            if (c >= '!' && c <= '~') return (char)(c + 0xFEE0);
            return c;
        }

        /// <summary>Decodifica una cadena de script: texto + etiquetas para cada byte de control.</summary>
        public string Decode(byte[] b, int off, int len) { return Decode(b, off, len, true); }
        /// <summary>Decodificacion "cruda" (sin etiquetas): para el inspector hexadecimal.</summary>
        public string DecodeRaw(byte[] b, int off, int len) { return Decode(b, off, len, false); }

        string Decode(byte[] b, int off, int len, bool script)
        {
            StringBuilder sb = new StringBuilder();
            int i = off, end = off + len;
            while (i < end)
            {
                if (i + 1 < end && ((b[i] >= 0x81 && b[i] <= 0x9F) || (b[i] >= 0xE0 && b[i] <= 0xFC)))
                {
                    ushort code = (ushort)((b[i] << 8) | b[i + 1]);
                    char x;
                    if (extraDec.TryGetValue(code, out x)) { sb.Append(x); i += 2; continue; }
                    string s;
                    try { s = strict.GetString(b, i, 2); } catch (Exception) { s = "\uFFFD"; }
                    for (int k = 0; k < s.Length; k++) sb.Append(ToNormal(s[k]));
                    i += 2;
                    continue;
                }
                byte v = b[i];
                if (!script) { sb.Append((char)v); i++; continue; }
                if (v == 0x0A) { sb.Append('\n'); i++; }
                else if (v == 0xFE && i + 1 < end && b[i + 1] <= 1) { sb.Append(b[i + 1] == 0 ? TagHero : TagHero2); i += 2; }
                else if (v == 0x11) { sb.Append(TagPage); i++; }
                else if (v == 0x08) { sb.Append(TagWin); i++; }
                else { sb.Append('{').Append(v.ToString("X2")).Append('}'); i++; }
            }
            return sb.ToString();
        }

        static byte[] TagBytes(string tag)
        {
            string t = tag.ToUpperInvariant();
            if (t == "HEROE") return new byte[] { 0xFE, 0x00 };
            if (t == "HEROE2") return new byte[] { 0xFE, 0x01 };
            if (t == "PAG") return new byte[] { 0x11 };
            if (t == "VENT") return new byte[] { 0x08 };
            if (t.Length == 2) return new byte[] { Convert.ToByte(t, 16) };
            return null;
        }

        /// <summary>Numero de bytes de control (saltos de linea + etiquetas) de un texto.</summary>
        public int CountControl(string s)
        {
            int n = 0;
            foreach (char c in s) if (c == '\n') n++;
            foreach (Match m in tagRx.Matches(s)) n += TagBytes(m.Groups[1].Value).Length;
            return n;
        }

        /// <summary>Rellena enc hasta len con espacios de ancho completo. Si el texto tiene opciones (0x0B),
        /// el relleno va antes del ultimo salto de pagina previo a las opciones, para no ensanchar la ultima opcion.</summary>
        /// <summary>Bytes de argumento que siguen a un codigo de control: 0x57 lleva 1, 0x0F lleva 2, 0xFE (nombre) lleva 1.</summary>
        static int ArgCount(byte b) { return b == 0x57 ? 1 : b == 0x0F ? 2 : b == 0xFE ? 1 : 0; }
        static bool Lead(byte b) { return (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC); }

        /// <summary>Corta un texto codificado en mensajes: 0x01 (cuando no es argumento de otro codigo) termina un
        /// mensaje, y el juego apunta a cada mensaje siguiente por su direccion. Cada trozo incluye su 0x01 final.</summary>
        public static List<byte[]> SplitMessages(byte[] b)
        {
            List<byte[]> list = new List<byte[]>();
            int st = 0;
            for (int i = 0; i < b.Length; i++)
            {
                byte x = b[i];
                if (Lead(x)) { i++; continue; }
                int n = ArgCount(x); if (n > 0) { i += n; continue; }
                if (x == 0x01) { byte[] s = new byte[i + 1 - st]; Array.Copy(b, st, s, 0, s.Length); list.Add(s); st = i + 1; }
            }
            byte[] last = new byte[b.Length - st]; Array.Copy(b, st, last, 0, last.Length); list.Add(last);
            return list;
        }

        /// <summary>Relleno de una entrada completa. Las cadenas "puras" de SLUS y MAIN (texto y saltos de linea, sin
        /// codigos de script) se leen por puntero hasta el 00: se rellenan con 00, sin espacios que el motor podria
        /// convertir en saltos de linea. El resto (scripts de TOWN/DUNGEON) con PadMessages.</summary>
        /// <remarks>next = byte que sigue a la entrada en el original. Solo es cadena "pura" si termina en 00: si sigue
        /// un codigo (p.ej. 11 {PAG}), es texto de un guion y un 00 de relleno seria una instruccion basura (se
        /// colgaba el tutorial de Kewne en la torre, v0.4b).</remarks>
        public static byte[] PadEntry(string file, byte[] orig, byte[] enc, int next)
        {
            if ((file == "SLUS_006.14" || file == "MAIN/MAIN.BIN") && next == 0 && IsPureString(orig))
            {
                if (enc.Length > orig.Length) return null;
                byte[] o = new byte[orig.Length];
                Array.Copy(enc, o, enc.Length);
                return o;
            }
            return PadMessages(orig, enc);
        }

        static bool IsPureString(byte[] b)
        {
            for (int i = 0; i < b.Length; i++)
            {
                if (Lead(b[i])) { i++; continue; }
                if (b[i] == 0xFE) { i++; continue; }
                if (b[i] != 0x0A) return false;
            }
            return true;
        }

        /// <summary>Rellena la traduccion manteniendo cada mensaje interno en la misma posicion que en el original.
        /// Devuelve null si algun mensaje traducido no cabe en el hueco de su original.</summary>
        public static byte[] PadMessages(byte[] orig, byte[] enc)
        {
            List<byte[]> so = SplitMessages(orig), st = SplitMessages(enc);
            if (so.Count != st.Count) return null;
            List<byte> o = new List<byte>();
            for (int i = 0; i < so.Count; i++)
            {
                byte[] a = so[i], t = st[i];
                bool term = t.Length > 0 && i < so.Count - 1;          // termina en 0x01: el relleno va antes
                int bodyLen = term ? t.Length - 1 : t.Length, slot = term ? a.Length - 1 : a.Length;
                if (bodyLen > slot) return null;
                byte[] body = new byte[bodyLen]; Array.Copy(t, 0, body, 0, bodyLen);
                byte[] ob = new byte[slot]; Array.Copy(a, 0, ob, 0, slot);
                o.AddRange(PadPages(body, ob));
                if (term) o.Add(0x01);
            }
            return o.ToArray();
        }

        /// <summary>Entradas en las que alguna pagina traducida no cabia en la suya y se relleno sin anclar paginas.</summary>
        public static List<string> PageFallbackLog = new List<string>();

        /// <summary>Rellena un mensaje dejando cada {PAG} (0x11) en la misma posicion que en el original: los guiones del
        /// pueblo saltan con direcciones absolutas (codigos 15, 17, 3E xx) al {PAG} o a lo que le sigue ({VENT}, salto,
        /// 0F...); si el {PAG} se mueve, el salto cae en mitad del texto y el juego se cuelga (escena de Guy al poner
        /// nombre). Si una pagina traducida no cabe en la original, se rellena el mensaje entero (y se anota).</summary>
        static byte[] PadPages(byte[] body, byte[] ob)
        {
            List<int> ca = PageCuts(ob), ct = PageCuts(body);
            if (ca.Count == 0 || ca.Count != ct.Count)
            {
                if (ca.Count > 0) { UnanchoredPages += ca.Count; Note(); }
                return PadTo(body, ob.Length, ob);
            }
            // Mayor numero de {PAG} anclados: cortes 0..n+1 (0 = inicio, n+1 = final, siempre anclados); entre dos
            // cortes anclados consecutivos, la traduccion debe caber en el original.
            int n = ca.Count;
            Func<int, int> A = k => k == 0 ? 0 : k == n + 1 ? ob.Length : ca[k - 1] + 1;
            Func<int, int> T = k => k == 0 ? 0 : k == n + 1 ? body.Length : ct[k - 1] + 1;
            // Puntuacion: primero evitar el relleno de ultimo recurso (descuadra ventanas y cursores), despues
            // anclar el mayor numero de {PAG}.
            const int NONE = int.MinValue;
            int[] best = new int[n + 2], prev = new int[n + 2];
            for (int j = 1; j <= n + 1; j++)
            {
                best[j] = NONE;
                for (int i = 0; i < j; i++)
                {
                    if (best[i] == NONE || T(j) - T(i) > A(j) - A(i)) continue;
                    bool endsPag = j <= n;
                    int sa0 = A(i), sa1 = endsPag ? A(j) - 1 : A(j), st0 = T(i), st1 = endsPag ? T(j) - 1 : T(j);
                    int v = best[i] + 1 - (NeedsLastResort(body, st0, st1, ob, sa0, sa1, i > 0) ? 1000 : 0);
                    if (v > best[j]) { best[j] = v; prev[j] = i; }
                }
            }
            if (best[n + 1] == NONE) return PadTo(body, ob.Length, ob);   // no cabe (no deberia pasar)
            List<int> chain = new List<int>();
            for (int j = n + 1; j > 0; j = prev[j]) chain.Insert(0, j);
            chain.Insert(0, 0);
            int anchored = chain.Count - 2;
            if (anchored < n) { UnanchoredPages += n - anchored; Note(); }
            List<byte> r = new List<byte>();
            for (int c = 0; c + 1 < chain.Count; c++)
            {
                int i = chain[c], j = chain[c + 1];
                // tramo [i, j): termina en el {PAG} del corte j (salvo el final), que queda en su sitio
                bool endsPag = j <= n;
                int sa0 = A(i), sa1 = endsPag ? A(j) - 1 : A(j), st0 = T(i), st1 = endsPag ? T(j) - 1 : T(j);
                byte[] sa = new byte[sa1 - sa0], s = new byte[st1 - st0];
                Array.Copy(ob, sa0, sa, 0, sa.Length); Array.Copy(body, st0, s, 0, s.Length);
                if (i > 0 && TryLastResort(s, sa)) { byte[] s2 = WithExtraVent(s, sa); if (s2 != null && !TryLastResort(s2, sa)) s = s2; }
                r.AddRange(PadTo(s, sa.Length, sa));
                if (endsPag) r.Add(0x11);
            }
            return r.ToArray();
        }

        /// <summary>Prueba (sin dejar rastro en los contadores) si rellenar ese tramo necesita el ultimo recurso.
        /// afterPag: el tramo va justo detras de un {PAG}; si empieza por {VENT} puede llevar un {VENT} extra
        /// ({PAG}{VENT}{VENT}, como en el original), 1 byte de relleno invisible.</summary>
        static bool NeedsLastResort(byte[] body, int st0, int st1, byte[] ob, int sa0, int sa1, bool afterPag)
        {
            byte[] sa = new byte[sa1 - sa0], s = new byte[st1 - st0];
            Array.Copy(ob, sa0, sa, 0, sa.Length); Array.Copy(body, st0, s, 0, s.Length);
            if (!TryLastResort(s, sa)) return false;
            byte[] s2 = afterPag ? WithExtraVent(s, sa) : null;
            return s2 == null || TryLastResort(s2, sa);
        }

        static bool TryLastResort(byte[] s, byte[] sa)
        {
            int lr = LastResort, logn = LastResortLog.Count;
            PadTo(s, sa.Length, sa);
            bool need = LastResort != lr;
            LastResort = lr; if (LastResortLog.Count > logn) LastResortLog.RemoveRange(logn, LastResortLog.Count - logn);
            return need;
        }

        /// <summary>{VENT} + tramo si el tramo empieza por un solo {VENT} y cabe; si no, null.</summary>
        static byte[] WithExtraVent(byte[] s, byte[] sa)
        {
            if (s.Length == 0 || s[0] != 0x08 || (s.Length > 1 && s[1] == 0x08) || s.Length + 1 > sa.Length) return null;
            byte[] s2 = new byte[s.Length + 1]; s2[0] = 0x08; Array.Copy(s, 0, s2, 1, s.Length);
            return s2;
        }

        /// <summary>{PAG} que no se han podido dejar en su sitio (la traduccion de esa pagina es mas larga).</summary>
        public static int UnanchoredPages;
        static void Note() { if (!PageFallbackLog.Contains(Current)) PageFallbackLog.Add(Current); }

        /// <summary>Posiciones de los {PAG} (0x11) que son codigo (no argumento ni segundo byte de una letra).</summary>
        static List<int> PageCuts(byte[] b)
        {
            List<int> r = new List<int>();
            for (int i = 0; i < b.Length; i++)
            {
                byte x = b[i];
                if (Lead(x)) { i++; continue; }
                int n = ArgCount(x); if (n > 0) { i += n; continue; }
                if (x == 0x11) r.Add(i);
            }
            return r;
        }

        /// <summary>Ancho maximo de una linea de dialogo en caracteres (el original nunca pasa de 31).</summary>
        public const int MaxLine = 31;

        /// <summary>Una linea de un texto codificado: [Start, End) sin el byte que la termina.</summary>
        public class Line { public int Start, End, Width; public bool Choice; public byte Term; }

        /// <summary>El relleno de espacios nunca pasa de esta columna: si el motor encuentra un espacio en la columna 30-31,
        /// salta de linea solo (el original casi nunca tiene espacios ahi) y aparece una linea de mas que empuja el texto
        /// y descuadra el cursor de las opciones.</summary>
        public const int PadMax = 29;

        /// <summary>Veces que el relleno tuvo que usar el ultimo recurso (para el informe).</summary>
        public static int LastResort;
        public static string Current = "";
        public static List<string> LastResortLog = new List<string>();

        /// <summary>Lineas por ventana de dialogo.</summary>
        public const int MaxLines = 3;

        /// <summary>Corta un texto codificado en lineas (0x0A salto, 0x11 pagina, 0x08 ventana, 0x01 fin de mensaje).
        /// Una linea es una opcion si empieza por 0x0B (tras 57 xx / 0F xx yy), o si es la primera, empieza por '['
        /// y hay opciones despues (la entrada empieza en mitad de una eleccion: el 0x0B quedo antes).</summary>
        public static List<Line> Lines(byte[] b)
        {
            List<Line> r = new List<Line>();
            Line cur = new Line(); bool seenGlyph = false;
            for (int i = 0; i < b.Length; i++)
            {
                byte x = b[i];
                if (Lead(x)) { seenGlyph = true; cur.Width++; i++; continue; }
                if (x == 0xFE) { seenGlyph = true; cur.Width += 8; i++; continue; }
                int n = ArgCount(x); if (n > 0) { i += n; continue; }
                if (x == 0x0B && !seenGlyph) { cur.Choice = true; continue; }
                if (x == 0x0A || x == 0x11 || x == 0x08 || x == 0x01)
                {
                    cur.End = i; cur.Term = x; r.Add(cur); cur = new Line(); cur.Start = i + 1; seenGlyph = false; continue;
                }
            }
            cur.End = b.Length; r.Add(cur);
            // primera linea "[..." sin 0x0B delante: es la primera opcion si luego hay mas opciones
            if (r.Count > 1 && !r[0].Choice)
            {
                int p = r[0].Start; while (p < r[0].End && !Lead(b[p])) p++;
                bool later = false; for (int k = 1; k < r.Count; k++) if (r[k].Choice) later = true;
                if (later && p + 1 < r[0].End && b[p] == 0x81 && b[p + 1] == 0x6D) r[0].Choice = true;
            }
            return r;
        }

        /// <summary>Avisos de maquetacion de un texto codificado (sin relleno): lineas de mas de MaxLine caracteres
        /// y ventanas con mas de MaxLines lineas de texto. origEnc (opcional) = original: solo se avisa de lo que
        /// el original no hacia ya. Devuelve "" si todo esta bien.</summary>
        public static string LayoutWarnings(byte[] enc, byte[] origEnc)
        {
            int maxO = 0, linesO = 0;
            if (origEnc != null) { List<Line> lo = Lines(origEnc); foreach (Line l in lo) maxO = Math.Max(maxO, l.Width); linesO = MaxWindowLines(lo); }
            List<Line> L = Lines(enc);
            List<string> w = new List<string>();
            int n = 0;
            foreach (Line l in L)
                if (l.Width > Math.Max(MaxLine, maxO)) { n++; if (n <= 2) w.Add((l.Choice ? "opción" : "línea") + " de " + l.Width + " caracteres (máx. " + MaxLine + ")"); }
            if (n > 2) w.Add("y " + (n - 2) + " más");
            int win = MaxWindowLines(L);
            if (win > Math.Max(MaxLines, linesO)) w.Add("ventana con " + win + " líneas (máx. " + MaxLines + ")");
            return string.Join("; ", w.ToArray());
        }

        /// <summary>Mayor numero de lineas de texto (no opciones) seguidas en una misma ventana (unidas por saltos de linea).</summary>
        static int MaxWindowLines(List<Line> L)
        {
            int best = 0, run = 0;
            for (int k = 0; k < L.Count; k++)
            {
                if (!L[k].Choice && L[k].Width > 0) run++;
                if (L[k].Term != 0x0A) { best = Math.Max(best, run); run = 0; }
            }
            return Math.Max(best, run);
        }

        /// <summary>Rellena enc hasta len bytes sin cambiar lo que se ve:
        /// 1) espacios al final de las lineas de texto, sin pasar de MaxLine caracteres
        ///    (las lineas de opciones [..] nunca se rellenan: el juego cuenta ese relleno como parte de la
        ///    opcion y desborda la ventana; asi se colgaba el hipodromo);
        /// 2) lineas en blanco al final de las ventanas que tengan menos de 3 lineas;
        /// 3) un {VENT} extra tras cada {PAG}{VENT} (el original ya usa {VENT}{VENT});
        /// 4) en ultimo caso, espacios en la ultima linea de texto aunque pase del ancho (si la entrada solo
        ///    tiene opciones, detras de ellas sin pasar del ancho que tenian en el original).</summary>
        public static byte[] PadTo(byte[] enc, int len) { return PadTo(enc, len, null); }

        public static byte[] PadTo(byte[] enc, int len, byte[] orig)
        {
            int rem = len - enc.Length;
            if (rem <= 0) return enc;
            List<Line> L = Lines(enc);
            int[] add = new int[L.Count];      // espacios (2 bytes) a anadir al final de cada linea
            int[] cap = new int[L.Count];
            for (int k = 0; k < L.Count; k++)
            {
                // las opciones nunca se rellenan: en el original no llevan espacios detras y el juego
                // cuenta el relleno como parte de la opcion (ventana desbordada: cuelgue del hipodromo)
                if (L[k].Choice) cap[k] = L[k].Width;
                else cap[k] = PadMax;
                // una linea vacia de texto (solo codigos) no se rellena: no anadir lineas visibles nuevas
                if (!L[k].Choice && L[k].Width == 0) cap[k] = 0;
            }
            bool progress = true;
            while (rem >= 2 && progress)
            {
                progress = false;
                for (int k = L.Count - 1; k >= 0 && rem >= 2; k--)
                    if (L[k].Width + add[k] < cap[k]) { add[k]++; rem -= 2; progress = true; }
            }
            // 2) lineas en blanco nuevas (salto + espacios) al final de las ventanas con menos de MaxLines lineas.
            //    Solo en ventanas nuevas (inicio, tras {VENT} o fin de mensaje) y sin opciones.
            List<int>[] extra = new List<int>[L.Count];
            for (int a1 = L.Count - 1; a1 >= 0 && rem >= 1; )
            {
                int a0 = a1; while (a0 > 0 && L[a0 - 1].Term == 0x0A) a0--;
                bool fresh = a0 == 0 || L[a0 - 1].Term == 0x08 || L[a0 - 1].Term == 0x01;
                int used = 0; bool choice = false;
                for (int k = a0; k <= a1; k++) { if (L[k].Width > 0) used++; if (L[k].Choice) choice = true; }
                int last = -1; for (int k = a1; k >= a0; k--) if (L[k].Width > 0) { last = k; break; }
                if (!choice && last >= 0 && used < MaxLines)
                {
                    // m lineas nuevas (1 byte de salto + 2 por espacio): se elige m para gastar exactamente rem
                    int slots = MaxLines - used, m = -1;
                    for (int t = 1; t <= slots; t++) if (rem - t >= 0 && ((rem - t) & 1) == 0 && (rem - t) / 2 <= t * PadMax) { m = t; break; }
                    if (m < 0 && rem > slots * (1 + 2 * PadMax)) m = slots;          // no basta: se llenan todas
                    if (m < 0) for (int t = slots; t >= 1; t--) if (rem - t >= 0) { m = t; break; }
                    if (m > 0)
                    {
                        int spaces = Math.Min((rem - m) / 2, m * PadMax);
                        if (extra[last] == null) extra[last] = new List<int>();
                        for (int t = 0; t < m; t++) { int sp = spaces / m + (t < spaces % m ? 1 : 0); extra[last].Add(sp); }
                        rem -= m + spaces * 2;
                    }
                }
                a1 = a0 - 1;
            }
            // 3) un {VENT} extra tras cada {PAG}{VENT} (el original ya usa {VENT}{VENT}), como mucho uno por sitio
            List<int> ventAt = new List<int>();
            for (int i = 0; i + 1 < enc.Length && rem > 0; i++)
            {
                if (Lead(enc[i])) { i++; continue; }
                int n = ArgCount(enc[i]); if (n > 0) { i += n; continue; }
                if (enc[i] == 0x11 && enc[i + 1] == 0x08)
                { ventAt.Add(i + 2); rem--; if (rem > 0) { ventAt.Add(i + 2); rem--; } }
            }
            // 4) ultimo recurso: espacios en la ultima linea de texto aunque se pase del ancho
            int target = -1;
            for (int k = L.Count - 1; k >= 0; k--) if (!L[k].Choice && L[k].Width > 0) { target = k; break; }
            if (target < 0 && rem >= 2)
            {
                // la entrada solo tiene opciones: espacios detras de cada opcion hasta el ancho que tenia esa
                // misma opcion en el original (primero las que no son la ultima)
                List<int> ow = new List<int>();
                if (orig != null) foreach (Line l in Lines(orig)) if (l.Choice) ow.Add(l.Width);
                List<int> ch = new List<int>(); for (int k = 0; k < L.Count; k++) if (L[k].Choice) ch.Add(k);
                for (int pass = 0; pass < 2 && rem >= 2; pass++)
                    for (int c = 0; c < ch.Count && rem >= 2; c++)
                    {
                        if ((pass == 0) == (c == ch.Count - 1)) continue;
                        int k = ch[c], lim = c < ow.Count ? Math.Min(ow[c], PadMax) : L[k].Width;
                        while (rem >= 2 && L[k].Width + add[k] < lim) { add[k]++; rem -= 2; }
                    }
            }
            if (target < 0) target = 0;
            if (rem >= 2) { LastResort++; LastResortLog.Add(Current + "\t" + rem); add[target] += rem / 2; rem -= (rem / 2) * 2; }
            int oddLine = target;      // byte impar suelto (0x20): en una linea que aun tenga sitio
            for (int k = L.Count - 1; k >= 0; k--)
                if (!L[k].Choice && L[k].Width > 0 && L[k].Width + add[k] < PadMax) { oddLine = k; break; }
            List<byte> o = new List<byte>(len);
            int pos = 0;
            for (int k = 0; k < L.Count; k++)
            {
                for (; pos < L[k].End; pos++) { o.Add(enc[pos]); foreach (int va in ventAt) if (va == pos + 1) o.Add(0x08); }
                for (int s = 0; s < add[k]; s++) { o.Add(0x81); o.Add(0x40); }
                if (extra[k] != null)
                    foreach (int sp in extra[k]) { o.Add(0x0A); for (int s = 0; s < sp; s++) { o.Add(0x81); o.Add(0x40); } }
                if (rem == 1 && k == oddLine) { o.Add(0x20); rem = 0; LastResortLog.Add(Current + "\timpar"); }
            }
            for (; pos < enc.Length; pos++) { o.Add(enc[pos]); foreach (int va in ventAt) if (va == pos + 1) o.Add(0x08); }
            return o.ToArray();
        }

        /// <summary>Codifica texto normal a bytes del juego. Lanza excepcion si hay un caracter sin representacion.</summary>
        public byte[] Encode(string text)
        {
            List<byte> o = new List<byte>();
            text = Simplify(text);
            // Bug del motor (confirmado en pantalla): si el PRIMER glifo dibujado tras abrir una
            // ventana/pagina es uno de los signos nuevos (¿ ¡), no se dibuja NI los ~4 siguientes,
            // dejando ver el contenido anterior (p.ej. la opcion "[No.]" de una eleccion previa).
            // Los tags de control (no dibujan nada, salvo {HEROE}) no cuentan como "ya se dibujo algo".
            bool winStart = true;
            for (int idx = 0; idx < text.Length; idx++)
            {
                char c = text[idx];
                if (c == '{')
                {
                    int close = text.IndexOf('}', idx + 1);
                    if (close > idx && close - idx <= 8)
                    {
                        Match m = tagRx.Match(text.Substring(idx, close - idx + 1));
                        if (m.Success && m.Length == close - idx + 1)
                        {
                            string tag = m.Groups[1].Value.ToUpperInvariant();
                            o.AddRange(TagBytes(tag));
                            idx = close;
                            if (tag == "PAG" || tag == "VENT") winStart = true;        // reabre ventana/pagina
                            else if (tag == "HEROE" || tag == "HEROE2") winStart = false; // dibuja el nombre
                            // otros tags (bytes de control sueltos: 01, 0F, 03, 57...) no dibujan nada: no tocan winStart
                            continue;
                        }
                    }
                }
                if (c == '\n') { o.Add(0x0A); continue; }   // solo mueve el cursor: no cuenta como dibujado
                if (winStart && (c == '¿' || c == '¡') && extraEnc.ContainsKey(c)) { winStart = false; continue; }
                winStart = false;
                ushort code;
                if (extraEnc.TryGetValue(c, out code)) { o.Add((byte)(code >> 8)); o.Add((byte)code); continue; }
                char w = ToWide(c);
                byte[] bs;
                try { bs = strict.GetBytes(new char[] { w }); }
                catch (Exception) { throw new InvalidDataException("Caracter no representable en el juego: '" + c + "' (U+" + ((int)c).ToString("X4") + "). Defínelo en charmap.txt."); }
                o.AddRange(bs);
            }
            return o.ToArray();
        }

        // ---- deteccion de texto -----------------------------------------------------------

        static bool IsGoodPair(byte a, byte b)
        {
            if (a == 0x81)
                return b >= 0x40 && b <= 0xAC && b != 0x7F;
            if (a == 0x82)
                return (b >= 0x4F && b <= 0x58) || (b >= 0x60 && b <= 0x79) || (b >= 0x81 && b <= 0x9A) || (b >= 0x9F && b <= 0xF1);
            if (a == 0x83)
                return (b >= 0x40 && b <= 0x96) && b != 0x7F;
            return false;
        }

        // bytes de control que pueden aparecer DENTRO de un dialogo (entre dos tramos de texto)
        static bool IsCtl(byte b) { return (b >= 0x01 && b <= 0x14) || b == 0x57; }

        /// <summary>
        /// Busca cadenas de texto de ancho completo. Une los tramos de un mismo dialogo (saltos de linea, paginas,
        /// nombre del heroe) hasta el byte 00 que marca el fin.
        /// </summary>
        public List<TextEntry> Scan(byte[] data, string file, int minChars)
        {
            List<TextEntry> res = new List<TextEntry>();
            int i = 0, n = data.Length;
            while (i < n - 3)
            {
                // inicio: opcionalmente uno o mas {HEROE} seguidos de texto
                int k = i, ctl = 0;
                while (k + 3 < n && data[k] == 0xFE && data[k + 1] <= 1) { k += 2; ctl += 2; }
                if (!IsGoodPair(data[k], data[k + 1])) { i++; continue; }
                int s = i; i = k;
                int chars = 0, alpha = 0, kana = 0;
                while (i < n - 1)
                {
                    if (IsGoodPair(data[i], data[i + 1]))
                    {
                        if (data[i] == 0x82 || data[i] == 0x83) alpha++;
                        if ((data[i] == 0x82 && data[i + 1] >= 0x9F) || data[i] == 0x83) kana++;
                        chars++; i += 2; continue;
                    }
                    // hueco de codigos de control seguido de mas texto: mismo dialogo
                    int g = i, gc = 0;
                    while (g < n && gc < 14)
                    {
                        if (IsCtl(data[g])) { g++; gc++; }
                        else if (data[g] == 0xFE && g + 1 < n && data[g + 1] <= 1) { g += 2; gc += 2; }
                        else break;
                    }
                    if (g > i && g + 1 < n && IsGoodPair(data[g], data[g + 1])) { ctl += g - i; i = g; continue; }
                    break;
                }
                if (chars >= minChars && alpha >= 2)
                {
                    TextEntry t = new TextEntry();
                    t.IsJapanese = kana * 4 > chars;
                    t.File = file; t.Offset = s; t.Length = i - s; t.CtlBytes = ctl;
                    // fin de dialogo: tras el texto (y sus posibles 11/08/0A finales) viene 00 (fin) o un salto de script 15/16/17
                    int j = i; while (j < n && (data[j] == 0x11 || data[j] == 0x08 || data[j] == 0x0A)) j++;
                    t.EndsDialog = j < n && (data[j] == 0x00 || (data[j] >= 0x15 && data[j] <= 0x17));
                    t.OrigBytes = new byte[t.Length];
                    Buffer.BlockCopy(data, s, t.OrigBytes, 0, t.Length);
                    t.Original = Decode(data, s, t.Length);
                    // ruido: solo kana/kanji (o error de decodificacion) cuentan como "no ingles"; simbolos de boton se conservan
                    if (!t.IsJapanese)
                        foreach (char ch in t.Original)
                            if ((ch >= '\u3040' && ch <= '\u9FFF') || ch == '\uFFFD') { t.IsJapanese = true; break; }
                    res.Add(t);
                }
            }
            return res;
        }

        /// <summary>Compara las etiquetas de control del original con las de la traduccion. Devuelve un aviso o "".</summary>
        public string CompareControl(string original, string translation)
        {
            Dictionary<string, int> a = TagCount(original), b = TagCount(translation);
            List<string> msg = new List<string>();
            foreach (string k in a.Keys) { int c; b.TryGetValue(k, out c); if (c < a[k]) msg.Add("faltan " + (a[k] - c) + " " + k); }
            foreach (string k in b.Keys) { int c; a.TryGetValue(k, out c); if (c < b[k]) msg.Add("sobran " + (b[k] - c) + " " + k); }
            return string.Join(", ", msg.ToArray());
        }

        Dictionary<string, int> TagCount(string s)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            foreach (Match m in tagRx.Matches(s))
            {
                string v = m.Groups[1].Value.ToUpperInvariant();
                string k = v.Length == 2 ? "códigos técnicos {xx}" : "{" + v + "}";   // los {01}{0F}{0B}... se agrupan
                int c; d.TryGetValue(k, out c); d[k] = c + 1;
            }
            return d;   // los saltos de linea no se comparan: dependen del texto
        }
    }

    public static class ProjectIO
    {
        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unesc(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static void Save(string path, List<TextEntry> entries, bool onlyTranslated)
        {
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                w.WriteLine("#file\toffset\tmaxbytes\toriginal\ttranslation");
                foreach (TextEntry e in entries)
                {
                    if (onlyTranslated && e.Translation.Length == 0) continue;
                    w.WriteLine(e.File + "\t" + e.Offset.ToString("X") + "\t" + e.Length + "\t" + Esc(e.Original) + "\t" + Esc(e.Translation));
                }
            }
        }

        /// <summary>Carga traducciones y las aplica a las entradas existentes. Devuelve cuantas coincidieron; unmatched = filas con traduccion sin entrada.</summary>
        public static int Load(string path, List<TextEntry> entries) { int u; return Load(path, entries, out u); }

        public static int Load(string path, List<TextEntry> entries, out int unmatched)
        {
            Dictionary<string, TextEntry> map = new Dictionary<string, TextEntry>();
            foreach (TextEntry e in entries) map[e.File + "|" + e.Offset.ToString("X")] = e;
            int ok = 0; unmatched = 0;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p.Length < 5) continue;
                TextEntry e;
                if (map.TryGetValue(p[0] + "|" + p[1].ToUpperInvariant(), out e)) { e.Translation = Unesc(p[4]); ok++; }
                else if (p[4].Length > 0) unmatched++;
            }
            return ok;
        }
    }
}
