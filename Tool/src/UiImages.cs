using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace AzTool
{
    /// <summary>
    /// Imagenes traducidas de la interfaz (carpeta "imagenes" de la traduccion). Cada PNG se llama
    /// ARCHIVO@OFFSET.png (ARCHIVO con '/' cambiado por '_', p.ej. MAIN_MAIN.BIN@224B24.png): 128 px de ancho,
    /// el gris de cada pixel es el indice de color (4 bpp: gris = indice*17; 8 bpp: gris = indice).
    /// Al crear el BIN se recomprimen y se escriben en su flujo original (deben caber en su hueco).
    /// </summary>
    public static class UiImages
    {
        public static string Apply(Project p, string dir)
        {
            if (!Directory.Exists(dir)) return "Imágenes: no hay carpeta " + Path.GetFileName(dir);
            int ok = 0; StringBuilder bad = new StringBuilder();
            foreach (string png in Directory.GetFiles(dir, "*@*.png"))
            {
                string n = Path.GetFileNameWithoutExtension(png);
                int at = n.LastIndexOf('@');
                string file = n.Substring(0, at).Replace('_', '/');
                int off;
                try { off = Convert.ToInt32(n.Substring(at + 1), 16); } catch (Exception) { bad.Append(" " + n + "(nombre)"); continue; }
                if (p.Find(file) == null) { bad.Append(" " + n + "(archivo)"); continue; }
                int consumed;
                byte[] orig = GfxCodec.TryDecode(p.GetOriginal(file), off, 1 << 20, out consumed);
                if (orig == null) { bad.Append(" " + n + "(no hay imagen)"); continue; }
                // Un flujo real no empieza con ceros: si los hay, el decodificador esta "atravesando" relleno
                // (que el juego puede usar como memoria) hasta la imagen de verdad, y escribir ahi lo corrompe.
                byte[] src = p.GetOriginal(file);
                if (src[off] == 0 && src[off + 1] == 0 && src[off + 2] == 0 && src[off + 3] == 0) { bad.Append(" " + n + "(empieza en ceros: offset falso)"); continue; }
                byte[] d = (byte[])orig.Clone();
                using (Bitmap bm = new Bitmap(png))
                {
                    int bpp = bm.Width * bm.Height == d.Length ? 8 : bm.Width * bm.Height == d.Length * 2 ? 4 : 0;
                    if (bm.Width != 128 || bpp == 0) { bad.Append(" " + n + "(tamaño)"); continue; }
                    for (int y = 0; y < bm.Height; y++)
                        for (int x = 0; x < 128; x++)
                        {
                            int g = bm.GetPixel(x, y).R;
                            if (bpp == 8) d[y * 128 + x] = (byte)g;
                            else { int v = Math.Min(15, (g + 8) / 17), i = y * 64 + (x >> 1); d[i] = (x & 1) == 0 ? (byte)((d[i] & 0xF0) | v) : (byte)((d[i] & 0x0F) | (v << 4)); }
                        }
                }
                byte[] enc = GfxCodec.Encode(d);
                if (enc.Length > consumed) { bad.Append(" " + n + "(no cabe " + enc.Length + ">" + consumed + ")"); continue; }
                p.Write(file, off, enc);
                ok++;
            }
            return "Imágenes: aplicadas=" + ok + (bad.Length > 0 ? " ERRORES:" + bad : "");
        }
    }
}
