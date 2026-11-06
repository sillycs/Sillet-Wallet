using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using QRCoder;

namespace Sillet.Mobile.UI
{
    public sealed class EqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value == null ? "" : value.ToString(), parameter == null ? "" : parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class QrConverter : IValueConverter
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrEmpty(text)) return null;
            Bitmap cached;
            if (Cache.TryGetValue(text, out cached)) return cached;
            try
            {
                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
                {
                    var png = new PngByteQRCode(data).GetGraphic(10);
                    var bitmap = new Bitmap(new MemoryStream(png));
                    if (Cache.Count >= 12) Cache.Clear();
                    Cache[text] = bitmap;
                    return bitmap;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class FileImageConverter : IValueConverter
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path)) return null;
            Bitmap bitmap;
            if (Cache.TryGetValue(path, out bitmap)) return bitmap;
            try
            {
                if (File.Exists(path))
                {
                    using (var stream = File.OpenRead(path)) bitmap = Bitmap.DecodeToWidth(stream, 96);
                }
                else bitmap = null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            {
                bitmap = null;
            }
            if (bitmap != null) Cache[path] = bitmap;
            return bitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class AssetImageConverter : IValueConverter
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = (value as string) ?? (parameter as string);
            if (string.IsNullOrEmpty(name)) return null;
            Bitmap bitmap;
            if (Cache.TryGetValue(name, out bitmap)) return bitmap;
            try
            {
                var uri = new Uri("avares://Sillet.Mobile/Assets/" + name + ".png");
                if (AssetLoader.Exists(uri))
                {
                    using (var stream = AssetLoader.Open(uri)) bitmap = Bitmap.DecodeToWidth(stream, 128);
                }
                else bitmap = null;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidOperationException || e is FileNotFoundException)
            {
                bitmap = null;
            }
            Cache[name] = bitmap;
            return bitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class HexBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Avalonia.Media.Color color;
            return value is string && Avalonia.Media.Color.TryParse((string)value, out color) ? new Avalonia.Media.SolidColorBrush(color) : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public sealed class IconKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string;
            if (string.IsNullOrEmpty(key) || Application.Current == null) return null;
            object found;
            return Application.Current.Resources.TryGetResource("Icon" + key, Application.Current.ActualThemeVariant, out found) ? found as Geometry : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
