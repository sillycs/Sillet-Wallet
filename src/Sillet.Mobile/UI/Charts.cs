// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI
{
    internal static class ChartBrushes
    {
        public static IBrush Find(Control control, string key, string fallback)
        {
            object value;
            if (control.TryFindResource(key, control.ActualThemeVariant, out value) && value is IBrush) return (IBrush)value;
            return new SolidColorBrush(Color.Parse(fallback));
        }

        public static IBrush Fade(IBrush brush, double top)
        {
            var solid = brush as ISolidColorBrush;
            var color = solid == null ? Colors.White : solid.Color;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * top), color.R, color.G, color.B), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
            return gradient;
        }

        public static void DrawLine(DrawingContext context, Rect bounds, IList<double> values, IBrush stroke, double thickness, bool fill)
        {
            if (values == null || values.Count < 2 || bounds.Width <= 0 || bounds.Height <= 0) return;
            var min = values.Min();
            var max = values.Max();
            var range = max - min;
            if (range <= 0) range = Math.Abs(max) > 0 ? Math.Abs(max) * 0.01 : 1;
            var pad = thickness + 2;
            var height = bounds.Height - pad * 2;
            var step = bounds.Width / (values.Count - 1);
            var points = new List<Point>(values.Count);
            for (int i = 0; i < values.Count; i++)
                points.Add(new Point(i * step, pad + height - (values[i] - min) / range * height));

            if (fill)
            {
                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(new Point(points[0].X, bounds.Height), true);
                    foreach (var p in points) g.LineTo(p);
                    g.LineTo(new Point(points[points.Count - 1].X, bounds.Height));
                    g.EndFigure(true);
                }
                context.DrawGeometry(Fade(stroke, 0.28), null, area);
            }

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(points[0], false);
                for (int i = 1; i < points.Count; i++) g.LineTo(points[i]);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(stroke, thickness, null, PenLineCap.Round, PenLineJoin.Round), line);
        }
    }

    public sealed class LineChart : Control
    {
        public static readonly StyledProperty<IReadOnlyList<ChartPoint>> PointsProperty =
            AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartPoint>>(nameof(Points));

        public static readonly StyledProperty<bool> IsUpProperty =
            AvaloniaProperty.Register<LineChart, bool>(nameof(IsUp), true);

        static LineChart()
        {
            AffectsRender<LineChart>(PointsProperty, IsUpProperty);
        }

        public IReadOnlyList<ChartPoint> Points
        {
            get { return GetValue(PointsProperty); }
            set { SetValue(PointsProperty, value); }
        }

        public bool IsUp
        {
            get { return GetValue(IsUpProperty); }
            set { SetValue(IsUpProperty, value); }
        }

        public override void Render(DrawingContext context)
        {
            var points = Points;
            if (points == null || points.Count < 2) return;
            var brush = IsUp ? ChartBrushes.Find(this, "PositiveBrush", "#3DD68C") : ChartBrushes.Find(this, "NegativeBrush", "#F2555A");
            ChartBrushes.DrawLine(context, new Rect(Bounds.Size), points.Select(p => p.Value).ToList(), brush, 2.2, true);
        }
    }

    public sealed class Sparkline : Control
    {
        public static readonly StyledProperty<double[]> ValuesProperty =
            AvaloniaProperty.Register<Sparkline, double[]>(nameof(Values));

        public static readonly StyledProperty<bool> IsUpProperty =
            AvaloniaProperty.Register<Sparkline, bool>(nameof(IsUp), true);

        static Sparkline()
        {
            AffectsRender<Sparkline>(ValuesProperty, IsUpProperty);
        }

        public double[] Values
        {
            get { return GetValue(ValuesProperty); }
            set { SetValue(ValuesProperty, value); }
        }

        public bool IsUp
        {
            get { return GetValue(IsUpProperty); }
            set { SetValue(IsUpProperty, value); }
        }

        private double[] _sampledFor;
        private List<double> _sampled;
        private IBrush _upBrush, _downBrush;

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _upBrush = null;
            _downBrush = null;
        }

        public override void Render(DrawingContext context)
        {
            var values = Values;
            if (values == null || values.Length < 2) return;
            if (!ReferenceEquals(values, _sampledFor))
            {
                var step = Math.Max(1, values.Length / 48);
                var sampled = new List<double>();
                for (int i = 0; i < values.Length; i += step) sampled.Add(values[i]);
                if (sampled[sampled.Count - 1] != values[values.Length - 1]) sampled.Add(values[values.Length - 1]);
                _sampled = sampled;
                _sampledFor = values;
            }
            IBrush brush;
            if (IsUp) brush = _upBrush ?? (_upBrush = ChartBrushes.Find(this, "PositiveBrush", "#3DD68C"));
            else brush = _downBrush ?? (_downBrush = ChartBrushes.Find(this, "NegativeBrush", "#F2555A"));
            ChartBrushes.DrawLine(context, new Rect(Bounds.Size), _sampled, brush, 1.5, false);
        }
    }

    public sealed class AllocationBar : Control
    {
        public static readonly StyledProperty<IEnumerable> ItemsProperty =
            AvaloniaProperty.Register<AllocationBar, IEnumerable>(nameof(Items));

        static AllocationBar()
        {
            AffectsRender<AllocationBar>(ItemsProperty);
        }

        public IEnumerable Items
        {
            get { return GetValue(ItemsProperty); }
            set { SetValue(ItemsProperty, value); }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ItemsProperty) return;
            var oldList = change.OldValue as System.Collections.Specialized.INotifyCollectionChanged;
            var newList = change.NewValue as System.Collections.Specialized.INotifyCollectionChanged;
            if (oldList != null) oldList.CollectionChanged -= OnItemsChanged;
            if (newList != null) newList.CollectionChanged += OnItemsChanged;
        }

        private void OnItemsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var slices = Items == null ? new List<SliceItem>() : Items.OfType<SliceItem>().Where(s => s.Value > 0).ToList();
            var bounds = new Rect(Bounds.Size);
            var radius = bounds.Height / 2;
            context.DrawRectangle(ChartBrushes.Find(this, "SurfacePressedBrush", "#222226"), null, bounds, radius, radius);
            var total = slices.Sum(s => s.Value);
            if (total <= 0) return;
            using (context.PushClip(new RoundedRect(bounds, radius)))
            {
                double x = 0;
                for (int i = 0; i < slices.Count; i++)
                {
                    var width = bounds.Width * slices[i].Value / total;
                    Color color;
                    if (!Color.TryParse(slices[i].Color, out color)) color = Colors.Gray;
                    var gap = i < slices.Count - 1 ? 2 : 0;
                    context.DrawRectangle(new SolidColorBrush(color), null, new Rect(x, 0, Math.Max(0, width - gap), bounds.Height));
                    x += width;
                }
            }
        }
    }

    public sealed class Gauge : Control
    {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<Gauge, double>(nameof(Value), 50);

        static Gauge()
        {
            AffectsRender<Gauge>(ValueProperty);
        }

        public double Value
        {
            get { return GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            var thickness = 7.0;
            var radius = Math.Min(size.Width / 2, size.Height) - thickness;
            if (radius <= 0) return;
            var center = new Point(size.Width / 2, size.Height - thickness / 2);
            var colors = new[] { "#F2555A", "#E8875B", "#E2B464", "#9BCB6A", "#3DD68C" };
            for (int i = 0; i < 5; i++)
            {
                var a0 = Math.PI + Math.PI * i / 5 + 0.04;
                var a1 = Math.PI + Math.PI * (i + 1) / 5 - 0.04;
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0)), false);
                    g.ArcTo(new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1)), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse(colors[i])), thickness, null, PenLineCap.Round), arc);
            }
            var value = Math.Max(0, Math.Min(100, Value));
            var angle = Math.PI + Math.PI * value / 100;
            var tip = new Point(center.X + (radius - 14) * Math.Cos(angle), center.Y + (radius - 14) * Math.Sin(angle));
            var text = ChartBrushes.Find(this, "TextBrush", "#F4F4F5");
            context.DrawLine(new Pen(text, 2.4, null, PenLineCap.Round), center, tip);
            context.DrawEllipse(text, null, center, 4.5, 4.5);
        }
    }
}
