// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Sillet.App.Services;

namespace Sillet.Mobile.UI
{
    public sealed class TrExtension : MarkupExtension
    {
        public TrExtension()
        {
        }

        public TrExtension(string text)
        {
            Text = text;
        }

        public string Text { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new ReflectionBindingExtension(nameof(Loc.Current))
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
            return BindingOperations.DoNothing;
        }
    }
}
