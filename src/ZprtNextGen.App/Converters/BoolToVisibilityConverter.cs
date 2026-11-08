#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZprtNextGen.App.Converters;

/// <summary>Преобразует <see langword="true"/> в <see cref="Visibility.Visible"/>, иначе в <see cref="Visibility.Collapsed"/>.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
