using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sillet.App.Services;

namespace Sillet.App.Controls
{
    public static class Anim
    {
        public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
            "Enter", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnEnterChanged));
        public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
            "Delay", typeof(double), typeof(Anim), new PropertyMetadata(0.0));
        public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
            "Offset", typeof(double), typeof(Anim), new PropertyMetadata(10.0));
        public static readonly DependencyProperty ScaleFromProperty = DependencyProperty.RegisterAttached(
            "ScaleFrom", typeof(double), typeof(Anim), new PropertyMetadata(1.0));
        public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
            "Stagger", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnEnterChanged));

        public static bool GetEnter(DependencyObject d) { return (bool)d.GetValue(EnterProperty); }
        public static void SetEnter(DependencyObject d, bool value) { d.SetValue(EnterProperty, value); }
        public static double GetDelay(DependencyObject d) { return (double)d.GetValue(DelayProperty); }
        public static void SetDelay(DependencyObject d, double value) { d.SetValue(DelayProperty, value); }
        public static double GetOffset(DependencyObject d) { return (double)d.GetValue(OffsetProperty); }
        public static void SetOffset(DependencyObject d, double value) { d.SetValue(OffsetProperty, value); }
        public static double GetScaleFrom(DependencyObject d) { return (double)d.GetValue(ScaleFromProperty); }
        public static void SetScaleFrom(DependencyObject d, double value) { d.SetValue(ScaleFromProperty, value); }
        public static bool GetStagger(DependencyObject d) { return (bool)d.GetValue(StaggerProperty); }
        public static void SetStagger(DependencyObject d, bool value) { d.SetValue(StaggerProperty, value); }

        private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var element = d as FrameworkElement;
            if (element == null || !(e.NewValue is bool) || !(bool)e.NewValue) return;
            element.IsVisibleChanged -= OnVisibleChanged;
            element.IsVisibleChanged += OnVisibleChanged;
            if (element.IsVisible) Play(element);
        }

        private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool && (bool)e.NewValue) Play((FrameworkElement)sender);
        }

        private static void Play(FrameworkElement element)
        {
            double delay = GetDelay(element);
            if (GetStagger(element)) delay += Math.Min(IndexInList(element), 14) * 35;

            double scaleFrom = GetScaleFrom(element);
            var translate = new TranslateTransform(0, GetOffset(element));
            var scale = new ScaleTransform(scaleFrom, scaleFrom);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            element.RenderTransform = group;
            element.Opacity = 0;

            var begin = TimeSpan.FromMilliseconds(delay);
            var duration = new Duration(TimeSpan.FromMilliseconds(scaleFrom < 1 ? 420 : 340));
            IEasingFunction ease = scaleFrom < 1 ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } : Ease;

            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = begin });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(GetOffset(element), 0, duration) { BeginTime = begin, EasingFunction = Ease });
            if (scaleFrom < 1)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleFrom, 1, duration) { BeginTime = begin, EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleFrom, 1, duration) { BeginTime = begin, EasingFunction = ease });
            }
        }

        private static int IndexInList(FrameworkElement element)
        {
            DependencyObject current = element;
            while (current != null && !(current is ItemsControl)) current = VisualTreeHelper.GetParent(current);
            var list = current as ItemsControl;
            if (list == null) return 0;
            var container = list.ContainerFromElement(element);
            return container == null ? 0 : Math.Max(0, list.ItemContainerGenerator.IndexFromContainer(container));
        }
    }

    public sealed class AnimatedAmount : TextBlock
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
            typeof(AnimatedAmount), new PropertyMetadata(0.0, OnValueChanged));
        public static readonly DependencyProperty CurrencyProperty = DependencyProperty.Register(nameof(Currency), typeof(string),
            typeof(AnimatedAmount), new PropertyMetadata("clp", OnTextInputChanged));
        public static readonly DependencyProperty HiddenProperty = DependencyProperty.Register(nameof(Hidden), typeof(bool),
            typeof(AnimatedAmount), new PropertyMetadata(false, OnTextInputChanged));
        private static readonly DependencyProperty CurrentProperty = DependencyProperty.Register("Current", typeof(double),
            typeof(AnimatedAmount), new PropertyMetadata(0.0, OnTextInputChanged));

        public AnimatedAmount()
        {
            Refresh();
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public string Currency
        {
            get { return (string)GetValue(CurrencyProperty); }
            set { SetValue(CurrencyProperty, value); }
        }

        public bool Hidden
        {
            get { return (bool)GetValue(HiddenProperty); }
            set { SetValue(HiddenProperty, value); }
        }

        private static void OnTextInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((AnimatedAmount)d).Refresh();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var amount = (AnimatedAmount)d;
            var to = (double)e.NewValue;
            if (double.IsNaN(to) || double.IsInfinity(to)) return;
            var from = (double)amount.GetValue(CurrentProperty);
            amount.BeginAnimation(CurrentProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private void Refresh()
        {
            var current = (double)GetValue(CurrentProperty);
            Text = Hidden ? Formatters.Hidden : Formatters.Fiat((decimal)current, Currency ?? "clp");
        }
    }
}
