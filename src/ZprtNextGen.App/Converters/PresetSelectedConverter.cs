#nullable enable

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.Converters;

/// <summary>Возвращает кисть выделения, если пресет совпадает с выбранным, иначе <see langword="null"/>.</summary>
public class PresetSelectedConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var preset = values[0] as Preset;
        var selected = values.Length > 1 ? values[1] as Preset : null;
        return preset is not null && preset.Equals(selected)
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E))
            : null!;
    }

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
