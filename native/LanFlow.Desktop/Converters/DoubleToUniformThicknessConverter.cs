using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LanFlow.Desktop.Converters;

public sealed class DoubleToUniformThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var divisor = parameter is string text &&
                      double.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1;
        return new Thickness(System.Convert.ToDouble(value, CultureInfo.InvariantCulture) / divisor);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
