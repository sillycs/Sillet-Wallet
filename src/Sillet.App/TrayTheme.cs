using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sillet.App
{
    public static class TrayTheme
    {
        public static void Apply(ContextMenuStrip menu)
        {
            var surface = Resource("SurfaceBrush", Color.FromArgb(17, 17, 19));
            var border = Resource("BorderStrongBrush", Color.FromArgb(58, 58, 65));
            var text = Resource("TextBrush", Color.FromArgb(244, 244, 245));
            var hover = Resource("SurfacePressedBrush", Color.FromArgb(34, 34, 38));
            menu.Renderer = new ToolStripProfessionalRenderer(new Colors(surface, border, hover)) { RoundedEdges = false };
            menu.BackColor = surface;
            menu.ForeColor = text;
            foreach (ToolStripItem item in menu.Items) item.ForeColor = text;
        }

        private static Color Resource(string key, Color fallback)
        {
            try
            {
                var brush = System.Windows.Application.Current.TryFindResource(key) as System.Windows.Media.SolidColorBrush;
                if (brush != null) return Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
            }
            catch (InvalidOperationException)
            {
            }
            return fallback;
        }

        private sealed class Colors : ProfessionalColorTable
        {
            private readonly Color _surface, _border, _hover;

            public Colors(Color surface, Color border, Color hover)
            {
                _surface = surface;
                _border = border;
                _hover = hover;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => _surface;
            public override Color MenuBorder => _border;
            public override Color MenuItemBorder => _hover;
            public override Color MenuItemSelected => _hover;
            public override Color ImageMarginGradientBegin => _surface;
            public override Color ImageMarginGradientMiddle => _surface;
            public override Color ImageMarginGradientEnd => _surface;
            public override Color CheckBackground => _hover;
            public override Color CheckSelectedBackground => _hover;
            public override Color CheckPressedBackground => _hover;
            public override Color SeparatorDark => _border;
            public override Color SeparatorLight => _border;
        }
    }
}
