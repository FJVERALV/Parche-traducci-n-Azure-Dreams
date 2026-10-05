using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AzTool
{
    /// <summary>CSV estilo RFC 4180 compatible con Excel (UTF-8 con BOM, campos entre comillas, saltos de linea dentro de campos).</summary>
    public static class Csv
    {
        public const char ExcelEs = ';';   // Excel en espanol usa ';' como separador

        static string Quote(string s, char d)
        {
            if (s == null) s = "";
            if (s.IndexOf(d) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0 || s.StartsWith(" ") || s.EndsWith(" "))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static void Write(string path, List<string[]> rows, char delim)
        {
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                foreach (string[] r in rows)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < r.Length; i++) { if (i > 0) sb.Append(delim); sb.Append(Quote(r[i], delim)); }
                    w.Write(sb.ToString()); w.Write("\r\n");
                }
            }
        }

        /// <summary>Lee un CSV detectando el separador (; , o tabulador) a partir de la cabecera.</summary>
        public static List<string[]> Read(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            int nl = text.IndexOfAny(new char[] { '\r', '\n' });
            string head = nl < 0 ? text : text.Substring(0, nl);
            char delim = ';'; int best = -1;
            foreach (char c in new char[] { ';', ',', '\t' }) { int n = head.Split(c).Length; if (n > best) { best = n; delim = c; } }

            List<string[]> rows = new List<string[]>();
            List<string> cur = new List<string>();
            StringBuilder f = new StringBuilder();
            bool q = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; }
                    else f.Append(c);
                }
                else if (c == '"') { q = true; any = true; }
                else if (c == delim) { cur.Add(f.ToString()); f.Length = 0; any = true; }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    if (any || f.Length > 0) { cur.Add(f.ToString()); rows.Add(cur.ToArray()); }
                    cur = new List<string>(); f.Length = 0; any = false;
                }
                else { f.Append(c); any = true; }
            }
            if (any || f.Length > 0) { cur.Add(f.ToString()); rows.Add(cur.ToArray()); }
            return rows;
        }

        public static int Col(string[] header, string name)
        {
            for (int i = 0; i < header.Length; i++) if (header[i].Trim().TrimStart('﻿').Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
