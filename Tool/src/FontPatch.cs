using System;
using System.Collections.Generic;
using System.Text;

namespace AzTool
{
    /// <summary>
    /// Anade al juego las letras del espanol (a e i o u con acento, u con dieresis, n/N con tilde, signos de apertura).
    ///
    /// Como dibuja el juego una letra (SLUS, func_8004D880 / func_8004D828 / func_8004D91C):
    ///   codigo SJIS -> byte "ASCII" -> celda de la fuente = byte - 0x20  (atlas 128x128 4bpp, 16x8 celdas de 8x16).
    ///   Numeros y letras se convierten por rango; el resto de codigos pasa por una tabla fija de 39 entradas
    ///   (claves u16 en 0x8007142C, valores u8 en 0x8007147C). Un codigo que no esta en la tabla sale como corazon.
    ///
    /// Que se cambia (sin tocar el dibujo de ningun simbolo):
    ///   1) En la tabla, 10 codigos de simbolos que ningun texto del juego usa (solo aparecen en el teclado de
    ///      nombres) pasan a apuntar a celdas que el juego nunca muestra (letras francesas/alemanas y celdas vacias).
    ///   2) En esas celdas se dibujan las letras del espanol.
    /// </summary>
    public static class FontPatch
    {
        // (archivo, offset del flujo comprimido de la fuente)
        public static readonly string[] Files = { "MAIN/MAIN.BIN", "DUNGEON/DUNGEON.BIN" };
        public static readonly int[] Offsets = { 0x2257D8, 0x4C27D8 };

        // tabla de conversion en el ejecutable (RAM 0x8007142C, text 0x8002D000 en el offset 0x800)
        const string Exe = "SLUS_006.14";
        const int KeysOff = 0x7142C - 0x2D000 + 0x800, ValsOff = KeysOff + 0x50, TableLen = 40;

        // codigo SJIS reutilizado, simbolo original, letra nueva, celda destino
        struct Slot { public ushort Code; public char Old; public char New; public int Cell; public Slot(ushort c, char o, char n, int cell) { Code = c; Old = o; New = n; Cell = cell; } }
        static readonly Slot[] Slots = {
            new Slot(0x814F, '^',      'á', 113),  // celda de la a con acento grave
            new Slot(0x8151, '_',      'é', 112),  // e con acento: ya existe
            new Slot(0x8160, '~',      'í', 118),  // i circunfleja
            new Slot(0x8162, '|',      'ó', 119),  // o circunfleja
            new Slot(0x8165, '`',      'ú', 115),  // u con acento grave
            new Slot(0x8194, '#',      'ü', 109),  // u con dieresis: ya existe
            new Slot(0x816F, '｛', 'ñ', 111),  // celda vacia
            new Slot(0x8170, '｝', 'Ñ', 123),  // celda vacia
            new Slot(0x818F, '￥', '¿', 126),  // ? inclinado (duplicado)
            new Slot(0x8190, '$',      '¡', 127),  // ! inclinado (duplicado)
        };

        public static string CharmapText
        {
            get
            {
                StringBuilder sb = new StringBuilder("# Letras del espanol (ver FontPatch.cs): codigo SJIS = letra\r\n");
                foreach (Slot s in Slots) sb.Append(s.Code.ToString("X4")).Append('=').Append(s.New).Append("\r\n");
                return sb.ToString();
            }
        }

        const int RowBytes = 64;

        static int Get(byte[] a, int x, int y) { int b = a[y * RowBytes + (x >> 1)]; return (x & 1) == 0 ? (b & 15) : (b >> 4); }
        static void Set(byte[] a, int x, int y, int v)
        {
            int i = y * RowBytes + (x >> 1);
            a[i] = (x & 1) == 0 ? (byte)((a[i] & 0xF0) | (v & 15)) : (byte)((a[i] & 0x0F) | ((v & 15) << 4));
        }
        static int CX(int cell) { return (cell % 16) * 8; }
        static int CY(int cell) { return (cell / 16) * 16; }

        static void Clear(byte[] a, int cell) { for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++) Set(a, CX(cell) + x, CY(cell) + y, 0); }
        static void CopyRows(byte[] dst, byte[] src, int from, int to, int y0, int y1)
        {
            for (int y = y0; y <= y1; y++) for (int x = 0; x < 8; x++) Set(dst, CX(to) + x, CY(to) + y, Get(src, CX(from) + x, CY(from) + y));
        }
        static void Draw(byte[] a, int cell, int y0, string[] rows)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int x = 0; x < 8; x++) if (rows[r][x] != '.') Set(a, CX(cell) + x, CY(cell) + y0 + r, rows[r][x] - '0');
        }

        /// <summary>Devuelve un atlas nuevo con las letras anadidas.</summary>
        public static byte[] Build(byte[] atlas)
        {
            byte[] f = (byte[])atlas.Clone();
            byte[] src = atlas;   // lectura siempre del original
            const int E_ACUTE = 112;

            // acento agudo (filas 0..4 de la e con acento) sobre el cuerpo que ya tiene la celda
            foreach (int cell in new int[] { 113, 118, 119, 115 }) CopyRows(f, src, E_ACUTE, cell, 0, 4);

            // n con tilde (cuerpo de la n, celda 78)
            Clear(f, 111);
            CopyRows(f, src, 78, 111, 5, 15);
            Draw(f, 111, 1, new string[] { "..88..8.", ".8..88.." });

            // N con tilde
            Clear(f, 123);
            Draw(f, 123, 0, new string[] {
                "..88..8.", ".8..88..", "........",
                ".88..88.", ".888.88.", ".888.88.", ".88888..", ".88.888.",
                ".88.888.", ".88..88.", ".88..88.", ".88..88.", ".88..88." });

            // signos de apertura: ? y ! girados 180 grados (filas 1..12)
            Action<int, int> rot = delegate(int srcCell, int dst)
            {
                Clear(f, dst);
                for (int y = 1; y <= 12; y++) for (int x = 0; x < 8; x++) Set(f, CX(dst) + (7 - x), CY(dst) + (13 - y), Get(src, CX(srcCell) + x, CY(srcCell) + y));
            };
            rot(31, 126);   // ?
            rot(1, 127);    // !
            return f;
        }

        /// <summary>Celda por defecto de las letras del espanol (si charmap.txt no indica @celda).</summary>
        public static int DefaultCell(ushort code)
        {
            foreach (Slot s in Slots) if (s.Code == code) return s.Cell;
            return -1;
        }

        /// <summary>Atlas de la fuente (128x128, 4bpp = 8192 bytes) tal y como esta ahora en el proyecto.</summary>
        public static byte[] LoadAtlas(Project p, bool original, out int consumed)
        {
            byte[] d = original ? p.GetOriginal(Files[0]) : p.Get(Files[0]);
            byte[] atlas = GfxCodec.TryDecode(d, Offsets[0], 65536, out consumed);
            return atlas != null && atlas.Length == 8192 ? atlas : null;
        }

        /// <summary>¿La fuente del proyecto es distinta de la original? (editada en la pestaña Fuente o ya parcheada)</summary>
        public static bool IsEdited(Project p)
        {
            for (int i = 0; i < Files.Length; i++)
            {
                int c; byte[] o = GfxCodec.TryDecode(p.GetOriginal(Files[i]), Offsets[i], 65536, out c);
                byte[] cur = p.Get(Files[i]), org = p.GetOriginal(Files[i]);
                for (int k = 0; k < c; k++) if (cur[Offsets[i] + k] != org[Offsets[i] + k]) return true;
            }
            return false;
        }

        /// <summary>Comprime el atlas y lo escribe en las dos copias de la fuente (MAIN y DUNGEON). Debe caber en el hueco original.</summary>
        public static string SaveAtlas(Project p, byte[] atlas)
        {
            byte[] enc = GfxCodec.Encode(atlas);
            int c2; byte[] chk = GfxCodec.TryDecode(enc, 0, 65536, out c2);
            if (chk == null || chk.Length != atlas.Length) return "el flujo recomprimido no se descomprime bien";
            for (int i = 0; i < atlas.Length; i++) if (chk[i] != atlas[i]) return "el flujo recomprimido difiere en el byte " + i;
            for (int i = 0; i < Files.Length; i++)
            {
                int consumed; byte[] o = GfxCodec.TryDecode(p.GetOriginal(Files[i]), Offsets[i], 65536, out consumed);
                if (o == null) return Files[i] + ": no hay una fuente valida en 0x" + Offsets[i].ToString("X");
                if (enc.Length > consumed) return Files[i] + ": la fuente comprimida (" + enc.Length + " bytes) no cabe en su hueco (" + consumed + " bytes). Simplifica algun dibujo.";
            }
            for (int i = 0; i < Files.Length; i++) p.Write(Files[i], Offsets[i], enc);
            return null;
        }

        /// <summary>Aplica el parche a una copia de la fuente en un fichero. Devuelve null si va bien, o el error.</summary>
        public static string Apply(Project p, string file, int off)
        {
            byte[] orig = p.GetOriginal(file);
            int consumed;
            byte[] atlas = GfxCodec.TryDecode(orig, off, 65536, out consumed);
            if (atlas == null || atlas.Length != 8192) return file + ": no hay una fuente valida en 0x" + off.ToString("X");
            byte[] neu = Build(atlas);
            byte[] enc = GfxCodec.Encode(neu);
            if (enc.Length > consumed) return file + ": la fuente comprimida (" + enc.Length + ") no cabe en " + consumed + " bytes";
            int c2; byte[] chk = GfxCodec.TryDecode(enc, 0, 65536, out c2);
            if (chk == null || chk.Length != neu.Length) return file + ": el flujo recomprimido no se descomprime bien";
            for (int i = 0; i < neu.Length; i++) if (chk[i] != neu[i]) return file + ": el flujo recomprimido difiere en el byte " + i;
            p.Write(file, off, enc);
            return null;
        }

        /// <summary>Las 40 entradas de la tabla de caracteres del ejecutable: codigo SJIS y valor actual (celda + 0x20).</summary>
        public static List<KeyValuePair<ushort, byte>> TableEntries(Project p, bool original)
        {
            byte[] exe = original ? p.GetOriginal(Exe) : p.Get(Exe);
            List<KeyValuePair<ushort, byte>> l = new List<KeyValuePair<ushort, byte>>();
            for (int i = 0; i < TableLen; i++)
            {
                ushort k = BitConverter.ToUInt16(exe, KeysOff + 2 * i);
                if (k == 0) continue;
                l.Add(new KeyValuePair<ushort, byte>(k, exe[ValsOff + i]));
            }
            return l;
        }

        /// <summary>Redirige en la tabla del ejecutable los codigos de charmap.txt a sus celdas (las demas, como en el original).</summary>
        public static string ApplyTable(Project p)
        {
            byte[] exe = p.GetOriginal(Exe);
            byte[] vals = new byte[TableLen];
            Array.Copy(exe, ValsOff, vals, 0, TableLen);
            foreach (KeyValuePair<ushort, KeyValuePair<char, int>> e in p.Codec.Charmap())
            {
                int cell = e.Value.Value >= 0 ? e.Value.Value : DefaultCell(e.Key);
                if (cell < 0 || cell > 127) return "tabla: falta la celda de '" + e.Value.Key + "' (" + e.Key.ToString("X4") + "): escribe " + e.Key.ToString("X4") + "=" + e.Value.Key + "@celda en charmap.txt";
                int k = -1;
                for (int i = 0; i < TableLen; i++) if (BitConverter.ToUInt16(exe, KeysOff + 2 * i) == e.Key) { k = i; break; }
                if (k < 0) return "tabla: el codigo " + e.Key.ToString("X4") + " ('" + e.Value.Key + "') no esta en la tabla del juego; usa uno de la lista de la pestaña Fuente";
                vals[k] = (byte)(cell + 0x20);
            }
            p.Write(Exe, ValsOff, vals);
            return null;
        }

        /// <summary>Al crear el BIN: si la fuente no se ha tocado, dibuja automaticamente las letras del espanol;
        /// si se edito en la pestaña Fuente, se respeta. La tabla siempre se genera desde charmap.txt.</summary>
        public static string ApplyAll(Project p)
        {
            StringBuilder sb = new StringBuilder();
            if (IsEdited(p)) sb.AppendLine("Fuente: editada en la herramienta (se mantiene)");
            else
                for (int i = 0; i < Files.Length; i++)
                {
                    string e = Apply(p, Files[i], Offsets[i]);
                    sb.AppendLine(e == null ? "Fuente: " + Files[i] + " OK" : "Fuente: ERROR " + e);
                }
            string t = ApplyTable(p);
            sb.AppendLine(t == null ? "Fuente: tabla de caracteres OK" : "Fuente: ERROR " + t);
            return sb.ToString();
        }

        /// <summary>Imagen del atlas modificado (para revisar las letras nuevas).</summary>
        public static byte[] Preview(byte[] atlas) { return Build(atlas); }
    }
}
