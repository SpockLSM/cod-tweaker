using System.Windows;

namespace Spock.AppKit;

/// <summary>Remembers a window's size between runs. The app stores the three values wherever it keeps its preferences.</summary>
public static class WindowPlacement
{
    /// <summary>Applies a saved size if it is sane for this screen; call before the window is shown.</summary>
    public static void Restore(Window window, double width, double height, bool maximized)
    {
        if (Fits(width, height, window.MinWidth, window.MinHeight, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
        {
            window.Width = width;
            window.Height = height;
        }
        if (maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>The values to save when the window closes.</summary>
    public static (double Width, double Height, bool Maximized) Capture(Window window)
    {
        bool max = window.WindowState == WindowState.Maximized;
        if (window.WindowState == WindowState.Normal) return (window.Width, window.Height, false);
        var b = window.RestoreBounds;
        return (b.Width, b.Height, max);
    }

    public static bool Fits(double width, double height, double minWidth, double minHeight, double screenWidth, double screenHeight) =>
        width >= minWidth && height >= minHeight && width <= screenWidth && height <= screenHeight && !double.IsNaN(width) && !double.IsNaN(height);
}
