using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.App
{
    public sealed class TrayMenu
    {
        private readonly Func<MainViewModel> _viewModel;
        private readonly Action<string> _open;
        private readonly Action _exit;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly PrivateFontCollection _fonts = new PrivateFontCollection();
        private readonly List<IntPtr> _fontMemory = new List<IntPtr>();
        private bool _fontsLoaded;
        private Palette _palette;

        private const int MenuWidth = 190;
        private const int HeaderHeight = 60;
        private const int AvatarSize = 36;
        private const int AvatarLeft = 10;
        private const int TextLeft = AvatarLeft + AvatarSize + 12;

        public TrayMenu(Func<MainViewModel> viewModel, Action<string> open, Action exit)
        {
            _viewModel = viewModel;
            _open = open;
            _exit = exit;
            _menu.ShowImageMargin = true;
            _menu.ShowCheckMargin = false;
            _menu.DropShadowEnabled = true;
            _menu.Padding = new Padding(0, 6, 0, 6);
            _menu.ImageScalingSize = new Size(16, 16);
            Rebuild();
        }

        public ContextMenuStrip Menu => _menu;

        public string Tooltip()
        {
            var vm = _viewModel();
            return vm != null && vm.IsUnlocked ? "Sillet  ·  " + Loc.T("Unlocked") : "Sillet  ·  " + Loc.T("Locked");
        }

        private sealed class Palette
        {
            public Color Surface, Border, Text, Muted, Hover, Accent;
            public bool Light;
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

        private Palette ReadPalette()
        {
            var surface = Resource("SurfaceBrush", Color.FromArgb(17, 17, 19));
            var palette = new Palette
            {
                Surface = surface,
                Border = Resource("BorderStrongBrush", Color.FromArgb(58, 58, 65)),
                Text = Resource("TextBrush", Color.FromArgb(244, 244, 245)),
                Muted = Resource("TextSecondaryBrush", Color.FromArgb(140, 140, 150)),
                Hover = Resource("SurfacePressedBrush", Color.FromArgb(34, 34, 38)),
                Accent = Resource("AccentBrush", Color.FromArgb(244, 244, 245))
            };
            palette.Light = surface.GetBrightness() > 0.6f;
            return palette;
        }

        private void LoadFonts()
        {
            if (_fontsLoaded) return;
            _fontsLoaded = true;
            foreach (var name in new[] { "Inter-Regular.otf", "Inter-SemiBold.otf" })
            {
                try
                {
                    var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Fonts/" + name, UriKind.Absolute));
                    if (info == null) continue;
                    byte[] data;
                    using (var stream = info.Stream)
                    using (var memory = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(memory);
                        data = memory.ToArray();
                    }
                    var pointer = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, pointer, data.Length);
                    _fontMemory.Add(pointer);
                    _fonts.AddMemoryFont(pointer, data.Length);
                }
                catch (Exception e) when (e is System.IO.IOException || e is ArgumentException || e is ExternalException)
                {
                }
            }
        }

        private Font UiFont(float size, bool bold)
        {
            LoadFonts();
            try
            {
                foreach (var family in _fonts.Families)
                    if (family.Name == "Inter") return new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
            }
            return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        private static Bitmap Glyph(string glyph, Color color)
        {
            var bitmap = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(color))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    Font font;
                    try
                    {
                        font = new Font("Segoe Fluent Icons", 10f, GraphicsUnit.Point);
                        if (font.Name != "Segoe Fluent Icons") font = new Font("Segoe MDL2 Assets", 10f, GraphicsUnit.Point);
                    }
                    catch (ArgumentException)
                    {
                        font = new Font("Segoe MDL2 Assets", 10f, GraphicsUnit.Point);
                    }
                    using (font) g.DrawString(glyph, font, brush, new RectangleF(0, 0, 20, 20), format);
                }
            }
            return bitmap;
        }

        private static Bitmap Lifebuoy(Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(color, 1.3f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(pen, 1.6f, 1.6f, 12.8f, 12.8f);
                g.DrawEllipse(pen, 5f, 5f, 6f, 6f);
                g.DrawLine(pen, 3.4f, 3.4f, 5.6f, 5.6f);
                g.DrawLine(pen, 10.4f, 10.4f, 12.6f, 12.6f);
                g.DrawLine(pen, 12.6f, 3.4f, 10.4f, 5.6f);
                g.DrawLine(pen, 5.6f, 10.4f, 3.4f, 12.6f);
            }
            return bitmap;
        }

        private Bitmap Logo(string name)
        {
            try
            {
                var file = name + (_palette.Light ? "black" : "white");
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + file + ".png", UriKind.Absolute));
                if (info == null) return null;
                using (var stream = info.Stream)
                using (var source = new Bitmap(stream))
                {
                    var bitmap = new Bitmap(16, 16);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawImage(source, new Rectangle(0, 0, 16, 16));
                    }
                    return bitmap;
                }
            }
            catch (Exception e) when (e is System.IO.IOException || e is ArgumentException)
            {
                return null;
            }
        }

        public void Refresh()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            _palette = ReadPalette();
            var old = new List<ToolStripItem>();
            foreach (ToolStripItem item in _menu.Items) old.Add(item);
            _menu.Items.Clear();
            foreach (var item in old)
            {
                if (item.Image != null) item.Image.Dispose();
                var header = item.Tag as HeaderInfo;
                if (header != null && header.Photo != null) header.Photo.Dispose();
                item.Dispose();
            }

            _menu.Renderer = new Renderer(_palette);
            _menu.BackColor = _palette.Surface;
            _menu.ForeColor = _palette.Text;
            _menu.Font = UiFont(9.25f, false);
            _menu.MinimumSize = new Size(Scale(MenuWidth), 0);

            var vm = _viewModel();
            bool unlocked = vm != null && vm.IsUnlocked;

            _menu.Items.Add(Header(vm, unlocked));
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(Item(Loc.T("Open Sillet"), "\uE8A7", () => _open("home"), true));
            if (unlocked)
            {
                _menu.Items.Add(Item(Loc.T("Settings"), "\uE713", () => _open("settings")));
                _menu.Items.Add(Item(Loc.T("Lock"), "\uE72E", () => { if (vm != null) vm.LockCommand.Execute(null); }));
            }
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Item(Loc.T("Exit"), "\uE711", _exit));
        }

        private static void Open(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
            }
        }

        private static int Scale(int value)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(value * g.DpiX / 96f);
        }

        private static Padding ItemPadding()
        {
            return new Padding(0, 7, 0, 7);
        }

        private ToolStripMenuItem Item(string text, string glyph, Action action, bool bold = false)
        {
            var item = new ToolStripMenuItem(text) { Padding = ItemPadding() };
            if (glyph != null) item.Image = Glyph(glyph, _palette.Text);
            if (bold) item.Font = UiFont(9.25f, true);
            item.Click += (s, e) => action();
            return item;
        }

        private sealed class HeaderInfo
        {
            public string Line;
            public string Initial;
            public Bitmap Photo;
        }

        private ToolStripLabel Header(MainViewModel vm, bool unlocked)
        {
            var info = new HeaderInfo { Line = unlocked && vm != null ? vm.ActiveProfileName : Loc.T("Locked"), Initial = vm != null && !string.IsNullOrEmpty(vm.ActiveProfileInitial) ? vm.ActiveProfileInitial : "S" };
            if (unlocked && vm != null) info.Photo = LoadPhoto(vm.ActiveProfilePhoto);
            return new ToolStripLabel("Sillet") { Enabled = true, AutoSize = false, Size = new Size(Scale(MenuWidth), Scale(HeaderHeight)), Tag = info, Font = UiFont(10.5f, true) };
        }

        private static Bitmap LoadPhoto(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                using (var stream = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path)))
                using (var source = new Bitmap(stream))
                {
                    int side = Math.Min(source.Width, source.Height);
                    var crop = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
                    var bitmap = new Bitmap(64, 64);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, new Rectangle(0, 0, 64, 64), crop, GraphicsUnit.Pixel);
                    }
                    return bitmap;
                }
            }
            catch (Exception e) when (e is System.IO.IOException || e is ArgumentException || e is UnauthorizedAccessException || e is OutOfMemoryException)
            {
                return null;
            }
        }

        private sealed class Renderer : ToolStripProfessionalRenderer
        {
            private readonly Palette _p;

            public Renderer(Palette palette) : base(new Colors(palette))
            {
                _p = palette;
                RoundedEdges = false;
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(_p.Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(_p.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var g = e.Graphics;
                if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed))
                {
                    using (var brush = new SolidBrush(_p.Hover))
                        g.FillRectangle(brush, new Rectangle(4, 0, e.Item.Width - 8, e.Item.Height));
                }
                var tag = e.Item.Tag as string;
                if (tag != null && tag.StartsWith("toggle:", StringComparison.Ordinal))
                {
                    bool on = tag == "toggle:on";
                    const int w = 30, h = 16;
                    var box = new Rectangle(e.Item.Width - w - 14, (e.Item.Height - h) / 2, w, h);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Pill(box))
                    {
                        if (on)
                        {
                            using (var fill = new SolidBrush(_p.Accent)) g.FillPath(fill, path);
                        }
                        else
                        {
                            using (var pen = new Pen(_p.Muted, 1.2f)) g.DrawPath(pen, path);
                        }
                    }
                    int knob = h - 6;
                    var knobBox = new Rectangle(on ? box.Right - knob - 3 : box.Left + 3, box.Top + 3, knob, knob);
                    using (var fill = new SolidBrush(on ? _p.Surface : _p.Muted)) g.FillEllipse(fill, knobBox);
                    g.SmoothingMode = SmoothingMode.Default;
                }
            }

            private static GraphicsPath Pill(Rectangle r)
            {
                var path = new GraphicsPath();
                int d = r.Height;
                path.AddArc(r.Left, r.Top, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 180);
                path.CloseFigure();
                return path;
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(Color.FromArgb(70, _p.Border))) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var info = e.Item.Tag as HeaderInfo;
                if (info != null)
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    var avatar = new Rectangle(AvatarLeft, (e.Item.Height - AvatarSize) / 2, AvatarSize, AvatarSize);
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddEllipse(avatar);
                        var state = g.Save();
                        g.SetClip(clip);
                        if (info.Photo != null) g.DrawImage(info.Photo, avatar);
                        else using (var brush = new SolidBrush(_p.Hover)) g.FillEllipse(brush, avatar);
                        g.Restore(state);
                    }
                    if (info.Photo == null)
                    {
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        using (var brush = new SolidBrush(_p.Text)) g.DrawString(info.Initial, e.Item.Font, brush, avatar, format);
                    }
                    using (var pen = new Pen(Color.FromArgb(90, _p.Border), 1f)) g.DrawEllipse(pen, avatar);

                    float top = (e.Item.Height - (e.Item.Font.GetHeight(g) + 15)) / 2f;
                    var textRect = new RectangleF(TextLeft, top, e.Item.Width - TextLeft - 10, e.Item.Font.GetHeight(g) + 2);

                    using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    {
                        using (var brush = new SolidBrush(_p.Text)) g.DrawString("Sillet", e.Item.Font, brush, textRect, format);

                        textRect.Y += e.Item.Font.GetHeight(g) + 1;
                        using (var small = new Font(e.Item.Font.FontFamily, 8.75f, FontStyle.Regular, GraphicsUnit.Point))
                        using (var muted = new SolidBrush(_p.Muted)) g.DrawString(info.Line, small, muted, textRect, format);
                    }
                    return;
                }
                e.TextColor = e.Item.Enabled ? _p.Text : _p.Muted;
                e.TextFormat &= ~TextFormatFlags.HidePrefix;
                e.TextFormat |= TextFormatFlags.NoPrefix;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = _p.Muted;
                base.OnRenderArrow(e);
            }
        }

        private sealed class Colors : ProfessionalColorTable
        {
            private readonly Palette _p;

            public Colors(Palette palette)
            {
                _p = palette;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => _p.Surface;
            public override Color MenuBorder => _p.Border;
            public override Color MenuItemBorder => _p.Hover;
            public override Color MenuItemSelected => _p.Hover;
            public override Color ImageMarginGradientBegin => _p.Surface;
            public override Color ImageMarginGradientMiddle => _p.Surface;
            public override Color ImageMarginGradientEnd => _p.Surface;
            public override Color SeparatorDark => _p.Border;
            public override Color SeparatorLight => _p.Border;
        }
    }
}