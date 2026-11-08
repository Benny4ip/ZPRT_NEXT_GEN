#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZprtNextGen.App.Converters;

/// <summary>Преобразует ссылку в <see cref="Visibility"/>: <c>null</c> — <see cref="Visibility.Collapsed"/>,
/// иначе — <see cref="Visibility.Visible"/>.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
