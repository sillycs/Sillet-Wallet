using System;
using System.Windows;
using System.Windows.Controls;

namespace Sillet.App.Controls
{
    public partial class CoinIcon : UserControl
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(string), typeof(CoinIcon));
        public static readonly DependencyProperty InitialProperty = DependencyProperty.Register(nameof(Initial), typeof(string), typeof(CoinIcon));
        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(CoinIcon),
            new PropertyMetadata(36.0, OnSizeChanged));

        public CoinIcon()
        {
            InitializeComponent();
        }

        public string Source
        {
            get { return (string)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        public string Initial
        {
            get { return (string)GetValue(InitialProperty); }
            set { SetValue(InitialProperty, value); }
        }

        public double Size
        {
            get { return (double)GetValue(SizeProperty); }
            set { SetValue(SizeProperty, value); }
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var icon = (CoinIcon)d;
            var size = (double)e.NewValue;
            icon.Host.Width = size;
            icon.Host.Height = size;
            icon.Letter.FontSize = Math.Max(9, size * 0.42);
        }
    }
}
