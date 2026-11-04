using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Sillet.App.Services;

namespace Sillet.App.Controls
{
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TrExtension : MarkupExtension
    {
        public TrExtension()
        {
        }

        public TrExtension(string text)
        {
            Text = text;
        }

        [ConstructorArgument("text")]
        public string Text { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding(nameof(Loc.Current))
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay,
                Converter = TrConverter.Instance,
                ConverterParameter = Text ?? ""
            };
            return binding.ProvideValue(serviceProvider);
        }
    }

    public sealed class TrConverter : IValueConverter
    {
        public static readonly TrConverter Instance = new TrConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = parameter as string ?? value as string;
            return Loc.T(text ?? "");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
