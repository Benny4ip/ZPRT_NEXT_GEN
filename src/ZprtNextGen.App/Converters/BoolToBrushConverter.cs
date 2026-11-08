#nullable enable

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZprtNextGen.App.Converters;

/// <summary>Преобразует <see cref="bool"/> в кисть: <c>true</c> — зелёная, <c>false</c> — красная.</summary>
public class BoolToBrushConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is true ? System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E) : System.Windows.Media.Color.FromRgb(0xE0, 0x1B, 0x2E);
        return new SolidColorBrush(color);
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
