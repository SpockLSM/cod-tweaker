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

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

/// <summary>Visible for true, a non-zero number or a non-empty string; Invert flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool truthy = value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        return truthy != Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class SizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long l ? (l >= 1024 * 1024 ? $"{l / 1048576.0:N1} MB" : $"{l / 1024.0:N1} KB") : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
