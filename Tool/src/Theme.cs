using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AzTool
{
    /// <summary>Paleta y utilidades de estilo oscuro para toda la aplicacion.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(30, 31, 34);
        public static readonly Color Panel = Color.FromArgb(43, 45, 49);
        public static readonly Color Panel2 = Color.FromArgb(52, 55, 60);
        public static readonly Color Border = Color.FromArgb(70, 73, 79);
        public static readonly Color Text = Color.FromArgb(223, 225, 229);
        public static readonly Color Dim = Color.FromArgb(150, 154, 162);
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color AccentDark = Color.FromArgb(52, 96, 190);
        public static readonly Color Good = Color.FromArgb(46, 125, 78);
        public static readonly Color GoodText = Color.FromArgb(120, 220, 150);
        public static readonly Color Bad = Color.FromArgb(140, 50, 55);
        public static readonly Color BadText = Color.FromArgb(255, 130, 130);
        public static readonly Color Warn = Color.FromArgb(230, 170, 60);

        public static Font UI = new Font("Segoe UI", 9.5f);
        public static Font UIBold = new Font("Segoe UI Semibold", 9.5f);
        public static Font Title = new Font("Segoe UI Semibold", 15f);
        public static Font Mono = new Font("Consolas", 10f);

        public static void Apply(Control c)
        {
            c.Font = UI;
            c.ForeColor = Text;
            if (c is TextBox) { c.BackColor = Panel2; ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; }
            else if (c is ComboBox) { c.BackColor = Panel2; ((ComboBox)c).FlatStyle = FlatStyle.Flat; }
            else if (c is NumericUpDown) { c.BackColor = Panel2; }
            else if (c is Button) { StyleButton((Button)c, (c.Tag as string) == "primary"); }
            else if (c is CheckBox) { c.BackColor = Color.Transparent; }
            else if (c is Label) { c.BackColor = Color.Transparent; }
            else if (c is ListView) { c.BackColor = Panel; }
            else if (c is DataGridView) { StyleGrid((DataGridView)c); }
            else if (c is TabControl) { }
            else if (c is Panel || c is UserControl || c is Form) { if (!(c is PictureBox)) c.BackColor = Bg; }
            foreach (Control ch in c.Controls) Apply(ch);
        }

        public static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = primary ? Accent : Panel2;
            b.FlatAppearance.MouseDownBackColor = AccentDark;
            b.BackColor = primary ? AccentDark : Panel;
            b.ForeColor = Text;
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            b.Height = Math.Max(b.Height, 28);
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Bg;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.GridColor = Color.FromArgb(55, 58, 63);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.ColumnHeadersDefaultCellStyle.BackColor = Panel2;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel2;
            g.ColumnHeadersDefaultCellStyle.Font = UIBold;
            g.ColumnHeadersHeight = 30;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.DefaultCellStyle.BackColor = Bg;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = AccentDark;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Font = UI;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(35, 37, 40);
            g.RowHeadersVisible = false;
            g.RowTemplate.Height = 24;
            g.AllowUserToResizeRows = false;
        }

        public static Button MakeButton(string text, int w, bool primary)
        {
            Button b = new Button { Text = text, Width = w, Height = 30 };
            b.Font = UI;
            if (primary) b.Tag = "primary";
            StyleButton(b, primary);
            return b;
        }

        public static Label MakeLabel(string text, bool dim)
        {
            Label l = new Label { Text = text, AutoSize = true, BackColor = Color.Transparent };
            l.ForeColor = dim ? Dim : Text;
            l.Font = UI;
            return l;
        }
    }

    /// <summary>Boton de la barra lateral.</summary>
    public class NavButton : Control
    {
        public bool Selected { get; set; }
        public string Glyph { get; set; }
        bool hover;
        public NavButton(string glyph, string text)
        {
            Glyph = glyph; Text = text;
            Height = 42; Dock = DockStyle.Top; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Color bg = Selected ? Theme.Panel2 : (hover ? Color.FromArgb(46, 48, 52) : Theme.Panel);
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            if (Selected) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 0, 3, Height);
            using (Font gf = new Font("Segoe UI Symbol", 12f))
            using (SolidBrush tb = new SolidBrush(Selected ? Color.White : Theme.Text))
            {
                g.DrawString(Glyph, gf, tb, 14, 9);
                g.DrawString(Text, Selected ? Theme.UIBold : Theme.UI, tb, 46, 11);
            }
        }
    }

    /// <summary>Barra de progreso fina con texto, para contadores de bytes y avance.</summary>
    public class MeterBar : Control
    {
        public double Value, Max = 1;
        public bool Over;
        public string Caption = "";
        public MeterBar()
        {
            Height = 20;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        public void Set(double v, double max, bool over, string caption)
        {
            Value = v; Max = max <= 0 ? 1 : max; Over = over; Caption = caption; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Panel2)) g.FillRectangle(b, 0, 0, Width, Height);
            int w = (int)(Width * Math.Min(1.0, Value / Max));
            Color c = Over ? Theme.Bad : Theme.Good;
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, 0, 0, w, Height);
            using (SolidBrush t = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(Caption, Theme.UI, t, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    /// <summary>Renderizador oscuro para menus y barras.</summary>
    public class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Panel2; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color MenuStripGradientBegin { get { return Theme.Panel; } }
        public override Color MenuStripGradientEnd { get { return Theme.Panel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Panel2; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Panel2; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Panel2; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Panel2; } }
        public override Color ToolStripGradientBegin { get { return Theme.Panel; } }
        public override Color ToolStripGradientMiddle { get { return Theme.Panel; } }
        public override Color ToolStripGradientEnd { get { return Theme.Panel; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color ButtonSelectedGradientBegin { get { return Theme.Panel2; } }
        public override Color ButtonSelectedGradientMiddle { get { return Theme.Panel2; } }
        public override Color ButtonSelectedGradientEnd { get { return Theme.Panel2; } }
        public override Color ButtonSelectedBorder { get { return Theme.Border; } }
        public override Color ButtonPressedGradientBegin { get { return Theme.AccentDark; } }
        public override Color ButtonPressedGradientMiddle { get { return Theme.AccentDark; } }
        public override Color ButtonPressedGradientEnd { get { return Theme.AccentDark; } }
    }
}
