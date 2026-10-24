using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Spock.AppKit;

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
            double d => d != 0,
            string s => s.Length > 0,
            null => false,
            _ => true,
        };
        return truthy != Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

/// <summary>A byte count as KB, MB or GB.</summary>
public sealed class SizeConverter : IValueConverter
{
    public static string Format(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824.0:N1} GB" : bytes >= 1L << 20 ? $"{bytes / 1048576.0:N1} MB" : $"{bytes / 1024.0:N1} KB";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long l ? Format(l) : value is int i ? Format(i) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
