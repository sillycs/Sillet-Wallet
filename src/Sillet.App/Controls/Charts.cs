using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.App.Controls
{
    internal static class Draw
    {
        public static readonly Typeface Regular = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public static readonly Typeface Bold = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static FormattedText Text(Visual visual, string text, double size, Brush brush, Typeface typeface = null) =>
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface ?? Regular, size, brush,
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);

        public static Color ColorOf(Brush brush)
        {
            var s = brush as SolidColorBrush;
            return s != null ? s.Color : Colors.White;
        }

        public static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        public static Point At(Point c, double r, double deg)
        {
            return new Point(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        }

        public static Brush Frozen(Brush brush)
        {
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        public static Brush Hex(string hex)
        {
            try { return Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))); }
            catch (FormatException) { return Brushes.Gray; }
        }

        public static Pen Pen(Brush brush, double thickness, bool round = true)
        {
            var pen = new Pen(brush, thickness);
            if (round) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; pen.LineJoin = PenLineJoin.Round; }
            if (pen.CanFreeze) pen.Freeze();
            return pen;
        }

        public static void Animate(UIElement element, DependencyProperty property, double ms, IEasingFunction ease = null) =>
            element.BeginAnimation(property, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    public sealed class LineChart : FrameworkElement
    {
        public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(nameof(Reveal), typeof(double),
            typeof(LineChart), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<ChartPoint>),
            typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => Draw.Animate((LineChart)d, RevealProperty, 900)));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush),
            typeof(LineChart), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(nameof(LabelBrush), typeof(Brush),
            typeof(LineChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TooltipBackgroundProperty = DependencyProperty.Register(nameof(TooltipBackground), typeof(Brush),
            typeof(LineChart), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TooltipForegroundProperty = DependencyProperty.Register(nameof(TooltipForeground), typeof(Brush),
            typeof(LineChart), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CurrencyProperty = DependencyProperty.Register(nameof(Currency), typeof(string),
            typeof(LineChart), new FrameworkPropertyMetadata("clp", FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<ChartPoint> Points { get => (IReadOnlyList<ChartPoint>)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
        public Brush TooltipBackground { get => (Brush)GetValue(TooltipBackgroundProperty); set => SetValue(TooltipBackgroundProperty, value); }
        public Brush TooltipForeground { get => (Brush)GetValue(TooltipForegroundProperty); set => SetValue(TooltipForegroundProperty, value); }
        public string Currency { get => (string)GetValue(CurrencyProperty); set => SetValue(CurrencyProperty, value); }
        public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

        private Point? _mouse;

        public LineChart()
        {
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.GetPosition(this);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
            var points = Points;
            if (points == null || points.Count < 2 || size.Width < 20 || size.Height < 40) return;

            var plot = new Rect(4, 26, Math.Max(1, size.Width - 8), Math.Max(1, size.Height - 52));
            double min = double.MaxValue, max = double.MinValue;
            int minIndex = 0, maxIndex = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var v = points[i].Value;
                if (v < min) { min = v; minIndex = i; }
                if (v > max) { max = v; maxIndex = i; }
            }
            double range = max - min;
            if (range <= 0) range = Math.Abs(max) * 0.01 + 1e-9;

            double X(int i) => plot.Left + i * plot.Width / (points.Count - 1);
            double Y(double v) => plot.Bottom - (v - min) / range * plot.Height;

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(new Point(X(0), Y(points[0].Value)), false, false);
                for (int i = 1; i < points.Count; i++) ctx.LineTo(new Point(X(i), Y(points[i].Value)), true, true);
            }
            line.Freeze();

            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(X(0), plot.Bottom + 26), true, true);
                for (int i = 0; i < points.Count; i++) ctx.LineTo(new Point(X(i), Y(points[i].Value)), true, true);
                ctx.LineTo(new Point(X(points.Count - 1), plot.Bottom + 26), true, true);
            }
            area.Freeze();

            var color = Draw.ColorOf(Stroke);
            var fill = Draw.Frozen(new LinearGradientBrush(Color.FromArgb(46, color.R, color.G, color.B),
                Color.FromArgb(0, color.R, color.G, color.B), new Point(0, 0), new Point(0, 1)));

            double reveal = Draw.Clamp(Reveal, 0, 1);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, plot.Left + plot.Width * reveal + 3, size.Height)));
            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, Draw.Pen(Stroke, 1.8), line);
            dc.Pop();

            if (reveal < 0.98) return;

            DrawExtreme(dc, X(maxIndex), Y(max) - 6, max, true, size.Width);
            DrawExtreme(dc, X(minIndex), Y(min) + 6, min, false, size.Width);

            if (_mouse.HasValue && _mouse.Value.X >= plot.Left && _mouse.Value.X <= plot.Right)
            {
                var m = _mouse.Value;
                int index = Draw.Clamp((int)Math.Round((m.X - plot.Left) / plot.Width * (points.Count - 1)), 0, points.Count - 1);
                var p = new Point(X(index), Y(points[index].Value));

                var guide = new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
                guide.Freeze();
                dc.DrawLine(guide, new Point(p.X, plot.Top - 10), new Point(p.X, size.Height));
                dc.DrawEllipse(Draw.Frozen(new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B))), null, p, 9, 9);
                dc.DrawEllipse(Stroke, null, p, 4, 4);

                bool shortRange = (points[points.Count - 1].Time - points[0].Time).TotalDays <= 2;
                var when = points[index].Time.ToString(shortRange ? "dd MMM, HH:mm" : "dd MMM yyyy", Formatters.Display);
                var price = Draw.Text(this, Formatters.Fiat((decimal)points[index].Value, Currency), 13, TooltipForeground, Draw.Bold);
                var date = Draw.Text(this, when, 11.5, TooltipForeground);
                double w = Math.Max(price.Width, date.Width) + 20, h = price.Height + date.Height + 14;
                double left = Draw.Clamp(p.X - w / 2, 0, size.Width - w);
                double top = p.Y - h - 16 < 0 ? p.Y + 16 : p.Y - h - 16;
                dc.DrawRoundedRectangle(TooltipBackground, null, new Rect(left, top, w, h), 8, 8);
                dc.DrawText(price, new Point(left + 10, top + 7));
                dc.PushOpacity(0.6);
                dc.DrawText(date, new Point(left + 10, top + 7 + price.Height));
                dc.Pop();
            }
        }

        private void DrawExtreme(DrawingContext dc, double x, double y, double value, bool above, double width)
        {
            var text = Draw.Text(this, Formatters.Fiat((decimal)value, Currency), 11, LabelBrush);
            double left = Draw.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width));
            dc.DrawText(text, new Point(left, above ? y - text.Height : y));
        }
    }

    public sealed class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(nameof(Reveal), typeof(double),
            typeof(Sparkline), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(object),
            typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => Draw.Animate((Sparkline)d, RevealProperty, 700)));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush),
            typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public object Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            var source = Values as IEnumerable<double>;
            if (source == null) return;
            var values = source.ToArray();
            if (values.Length < 2) return;
            int step = Math.Max(1, values.Length / 60);
            var sampled = values.Where((v, i) => i % step == 0 || i == values.Length - 1).ToArray();
            double min = sampled.Min(), max = sampled.Max(), range = max - min;
            if (range <= 0) range = 1;
            double w = RenderSize.Width, h = RenderSize.Height - 2;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = 0; i < sampled.Length; i++)
                {
                    var p = new Point(i * w / (sampled.Length - 1), 1 + h - (sampled[i] - min) / range * h);
                    if (i == 0) ctx.BeginFigure(p, false, false);
                    else ctx.LineTo(p, true, true);
                }
            }
            geometry.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w * Draw.Clamp(Reveal, 0, 1) + 2, RenderSize.Height)));
            dc.DrawGeometry(null, Draw.Pen(Stroke, 1.4), geometry);
            dc.Pop();
        }
    }

    public sealed class DonutChart : FrameworkElement
    {
        public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(nameof(Slices), typeof(IEnumerable),
            typeof(DonutChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSlicesChanged));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double),
            typeof(DonutChart), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush),
            typeof(DonutChart), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double),
            typeof(DonutChart), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable Slices { get => (IEnumerable)GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

        private string _signature = "";

        private static void OnSlicesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = (DonutChart)d;
            var oldList = e.OldValue as INotifyCollectionChanged;
            if (oldList != null) oldList.CollectionChanged -= chart.OnCollectionChanged;
            var newList = e.NewValue as INotifyCollectionChanged;
            if (newList != null) newList.CollectionChanged += chart.OnCollectionChanged;
            chart.AnimateIfChanged();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => AnimateIfChanged();
        private void AnimateIfChanged()
        {
            var signature = string.Join("|", Slices == null ? Enumerable.Empty<string>() : Slices.OfType<SliceItem>().Select(s => s.Label + s.PercentText));
            if (signature == _signature) { InvalidateVisual(); return; }
            _signature = signature;
            Draw.Animate(this, ProgressProperty, 1000, new QuinticEase { EasingMode = EasingMode.EaseOut });
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(RenderSize.Width, RenderSize.Height);
            if (size < 10) return;
            var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
            double radius = (size - Thickness) / 2;
            dc.DrawEllipse(null, Draw.Pen(TrackBrush, Thickness, false), center, radius, radius);

            var slices = Slices == null ? new List<SliceItem>() : Slices.OfType<SliceItem>().Where(s => s.Value > 0).ToList();
            double total = slices.Sum(s => s.Value);
            if (total <= 0) return;

            double progress = Draw.Clamp(Progress, 0, 1);
            if (slices.Count == 1 && progress >= 0.999)
            {
                dc.DrawEllipse(null, Draw.Pen(Draw.Hex(slices[0].Color), Thickness, false), center, radius, radius);
                return;
            }

            const double gap = 2.5;
            double start = -90;
            foreach (var slice in slices)
            {
                double sweep = slice.Value / total * 360 * progress;
                double drawn = sweep - gap;
                if (drawn > 0.3) DrawArc(dc, center, radius, start + gap / 2, drawn, Draw.Pen(Draw.Hex(slice.Color), Thickness, false));
                start += sweep;
            }
        }

        private static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Draw.At(c, r, startDeg), false, false);
                ctx.ArcTo(Draw.At(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    public sealed class FearGreedGauge : FrameworkElement
    {
        private static readonly string[] Segments = { "#F2555A", "#E8875A", "#D9C26A", "#8FCB7A", "#3DD68C" };

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
            typeof(FearGreedGauge), new FrameworkPropertyMetadata(50.0, OnValueChanged));
        public static readonly DependencyProperty NeedleBrushProperty = DependencyProperty.Register(nameof(NeedleBrush), typeof(Brush),
            typeof(FearGreedGauge), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double),
            typeof(FearGreedGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Brush NeedleBrush { get => (Brush)GetValue(NeedleBrushProperty); set => SetValue(NeedleBrushProperty, value); }

        public FearGreedGauge()
        {
            Loaded += (s, e) => Swing(Value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((FearGreedGauge)d).Swing((double)e.NewValue);

        private void Swing(double to)
        {
            if (!IsLoaded) return;
            BeginAnimation(ShownProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(1400))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 },
            });
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double thickness = 8;
            double radius = Math.Min(RenderSize.Width / 2, RenderSize.Height) - thickness / 2 - 2;
            if (radius < 10) return;
            var center = new Point(RenderSize.Width / 2, RenderSize.Height - 4);

            double segment = 180.0 / Segments.Length;
            for (int i = 0; i < Segments.Length; i++)
            {
                double start = 180 + i * segment + 2, end = 180 + (i + 1) * segment - 2;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(Draw.At(center, radius, start), false, false);
                    ctx.ArcTo(Draw.At(center, radius, end), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                geometry.Freeze();
                dc.PushOpacity(0.85);
                dc.DrawGeometry(null, Draw.Pen(Draw.Hex(Segments[i]), thickness), geometry);
                dc.Pop();
            }

            double shown = (double)GetValue(ShownProperty);
            double angle = 180 + Draw.Clamp(shown, -5, 105) / 100 * 180;
            dc.DrawLine(Draw.Pen(NeedleBrush, 2.5), center, Draw.At(center, radius - thickness - 8, angle));
            dc.DrawEllipse(NeedleBrush, null, center, 5, 5);
        }
    }
}
