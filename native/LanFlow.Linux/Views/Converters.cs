using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LanFlow.Desktop.Views;

public sealed class FirstCharConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && text.Length > 0)
        {
            return text[0].ToString();
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class NotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
