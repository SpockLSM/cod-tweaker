using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoDTweaker.Services;

namespace CoDTweaker;

public partial class App : Application
{
    /// <summary>Set by "--players &lt;folder&gt;" on the command line: use this folder instead of the saved one.</summary>
    public static string? PlayersFolderOverride { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int p = Array.IndexOf(e.Args, "--players");
        if (p >= 0 && p + 1 < e.Args.Length) PlayersFolderOverride = e.Args[p + 1];

        // "--theme dark|light" for documentation; otherwise the saved choice, or whatever Windows is set to.
        int t = Array.IndexOf(e.Args, "--theme");
        Theme.Apply(Theme.FromSetting(t >= 0 && t + 1 < e.Args.Length ? e.Args[t + 1] : AppSettings.Load().Theme));

        var window = new MainWindow();
        window.Show();

        // Hidden helper for documentation: CoDTweaker.exe --screenshot out.png
        int i = Array.IndexOf(e.Args, "--screenshot");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            string target = e.Args[i + 1];
            window.ContentRendered += async (_, _) =>
            {
                await Task.Delay(1200);
                SaveScreenshot(window, target);
                Shutdown();
            };
        }
    }

    static void SaveScreenshot(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        double w = root.ActualWidth + root.Margin.Left + root.Margin.Right;
        double h = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
        // Paint through a brush so the element's margin doesn't shift the capture.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.Margin.Left, root.Margin.Top, root.ActualWidth, root.ActualHeight));
        }
        var bmp = new RenderTargetBitmap((int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
