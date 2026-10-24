using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CoDTweaker.Models;

namespace CoDTweaker;

/// <summary>Visible when the setting's kind equals the converter parameter ("Bool", "Number", "Choice", "Text").</summary>
public sealed class KindToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SettingKind k && Enum.TryParse<SettingKind>(parameter as string, out var want) && k == want
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
