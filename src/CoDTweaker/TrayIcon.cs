using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WF = System.Windows.Forms;

namespace CoDTweaker;

/// <summary>The tray icon the app lives in while the Guard watches for the game to exit.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WF.NotifyIcon _icon;
    readonly WF.ToolStripMenuItem _guardItem;
    readonly Icon _drawn;
    readonly IntPtr _hIcon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<bool>? GuardToggled;

    public TrayIcon(bool guardOn)
    {
        (_drawn, _hIcon) = MakeIcon();
        var menu = new WF.ContextMenuStrip();
        var open = new WF.ToolStripMenuItem("Open CoD Tweaker");
        open.Click += (_, _) => OpenRequested?.Invoke();
        _guardItem = new WF.ToolStripMenuItem("Guard pinned settings") { CheckOnClick = true, Checked = guardOn };
        _guardItem.CheckedChanged += (_, _) => GuardToggled?.Invoke(_guardItem.Checked);
        var exit = new WF.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(_guardItem);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = new WF.NotifyIcon { Icon = _drawn, Text = "CoD Tweaker", ContextMenuStrip = menu, Visible = false };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public bool Visible { get => _icon.Visible; set => _icon.Visible = value; }

    public void SetGuard(bool on) { if (_guardItem.Checked != on) _guardItem.Checked = on; }

    public void Notify(string title, string text)
    {
        if (!_icon.Visible) return;
        try { _icon.ShowBalloonTip(5000, title, text, WF.ToolTipIcon.Info); } catch { }
    }

    /// <summary>A blue rounded square with a white C, drawn at run time so the app needs no icon file.</summary>
    static (Icon, IntPtr) MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var path = new GraphicsPath();
            var r = new Rectangle(1, 1, 30, 30); int d = 10;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
            g.FillPath(fill, path);
            using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("CT", font, Brushes.White, new RectangleF(0, 1, 32, 32), fmt);
        }
        IntPtr h = bmp.GetHicon();
        return (Icon.FromHandle(h), h);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _drawn.Dispose();
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);
}
