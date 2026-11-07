// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Sillet.App.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is Visibility && (Visibility)value == Visibility.Visible;
            return visible ^ Invert;
        }
    }

    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            bool hasValue = s != null ? s.Length > 0 : value != null;
            return hasValue ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class EqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), parameter as string, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class PathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception e) when (e is IOException || e is NotSupportedException || e is UriFormatException || e is InvalidOperationException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class QrCodeConverter : IValueConverter
    {
        private static readonly byte[] Dark = { 0x16, 0x16, 0x1A, 0xFF };
        private static readonly byte[] Light = { 0xFF, 0xFF, 0xFF, 0xFF };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrEmpty(text)) return null;
            byte[] png;
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
            {
                png = new PngByteQRCode(data).GetGraphic(12, Dark, Light, false);
            }
            var image = new BitmapImage();
            using (var stream = new MemoryStream(png))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var hex = value as string;
                if (hex != null)
                {
                    var converted = ColorConverter.ConvertFromString(hex);
                    if (converted is Color)
                    {
                        var brush = new SolidColorBrush((Color)converted);
                        brush.Freeze();
                        return brush;
                    }
                }
            }
            catch (FormatException)
            {
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public static class ResourceImages
    {
        private static readonly System.Collections.Generic.Dictionary<string, BitmapImage> Cache = new System.Collections.Generic.Dictionary<string, BitmapImage>();

        public static BitmapImage Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            BitmapImage cached;
            if (Cache.TryGetValue(name, out cached)) return cached;
            BitmapImage image = null;
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name + ".png", UriKind.Absolute));
                if (info != null)
                {
                    using (var stream = info.Stream)
                    {
                        image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is NotSupportedException || e is UriFormatException || e is InvalidOperationException)
            {
                image = null;
            }
            Cache[name] = image;
            return image;
        }
    }

    public sealed class ResourceImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ResourceImages.Load(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class ResourceMissingToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ResourceImages.Load(value as string) == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public sealed class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }
}
