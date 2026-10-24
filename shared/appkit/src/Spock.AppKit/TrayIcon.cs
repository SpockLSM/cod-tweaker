using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Spock.AppKit;

/// <summary>
/// A notification-area icon built on Shell_NotifyIcon, so an app needs neither Windows Forms nor an
/// extra package. The icon is the exe's own (set ApplicationIcon in the project), the menu is a WPF
/// ContextMenu, and balloons go through <see cref="Notify"/>.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const uint WmTrayCallback = 0x0400 + 1;  // WM_USER + 1
    const int WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205, WmLButtonUp = 0x0202, WmContextMenu = 0x007B;
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 0x10;
    const uint NiifInfo = 1;

    readonly HwndSource _source;
    readonly uint _taskbarCreated;
    readonly IntPtr _hIcon;
    readonly bool _ownsIcon;
    string _tooltip;
    bool _added;
    bool _disposed;

    /// <summary>Raised on a double-click (and a single left click).</summary>
    public event Action? Activated;

    /// <summary>The menu shown on right-click. Its DataContext is set to <see cref="MenuContext"/> when opened.</summary>
    public ContextMenu? ContextMenu { get; set; }
    public object? MenuContext { get; set; }

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        var p = new HwndSourceParameters("SpockAppKitTrayIcon") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        (_hIcon, _ownsIcon) = LoadAppIcon();
    }

    public bool Visible
    {
        get => _added;
        set { if (value) Add(); else Remove(); }
    }

    public string Tooltip
    {
        get => _tooltip;
        set { _tooltip = value; if (_added) Notify(NimModify, NifTip); }
    }

    /// <summary>A balloon or toast from the icon. Nothing happens while the icon is hidden.</summary>
    public void Notify(string title, string text)
    {
        if (!_added || _disposed) return;
        var d = Data(NifInfo);
        d.szInfoTitle = Trim(title, 63);
        d.szInfo = Trim(text, 255);
        d.dwInfoFlags = NiifInfo;
        Shell_NotifyIconW(NimModify, ref d);
    }

    void Add()
    {
        if (_added || _disposed) return;
        var d = Data(NifMessage | NifIcon | NifTip);
        _added = Shell_NotifyIconW(NimAdd, ref d);
    }

    void Remove()
    {
        if (!_added) return;
        var d = Data(0);
        Shell_NotifyIconW(NimDelete, ref d);
        _added = false;
    }

    void Notify(uint message, uint flags)
    {
        var d = Data(flags);
        Shell_NotifyIconW(message, ref d);
    }

    NotifyIconData Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _source.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WmTrayCallback,
        hIcon = _hIcon,
        szTip = Trim(_tooltip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTrayCallback)
        {
            switch ((int)lParam & 0xFFFF)
            {
                case WmLButtonDblClk:
                case WmLButtonUp:
                    Activated?.Invoke();
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated && _added)
        {
            _added = false;   // Explorer restarted: the icon is gone, put it back
            Add();
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        var menu = ContextMenu;
        if (menu == null) return;
        // A popup from a message-only window closes on an outside click only once that window is in the foreground.
        SetForegroundWindow(_source.Handle);
        menu.DataContext = MenuContext;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        menu.Closed += OnMenuClosed;
    }

    void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        ((ContextMenu)sender).Closed -= OnMenuClosed;
        PostMessage(_source.Handle, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>The exe's own icon; the stock application icon when the exe has none.</summary>
    static (IntPtr, bool) LoadAppIcon()
    {
        try
        {
            string exe = Shell.ExePath;
            if (exe.Length > 0)
            {
                IntPtr small = IntPtr.Zero, large = IntPtr.Zero;
                if (ExtractIconExW(exe, 0, out large, out small, 1) > 0)
                {
                    if (small != IntPtr.Zero && large != IntPtr.Zero) DestroyIcon(large);
                    var h = small != IntPtr.Zero ? small : large;
                    if (h != IntPtr.Zero) return (h, true);
                }
            }
        }
        catch { }
        return (LoadIconW(IntPtr.Zero, new IntPtr(32512) /* IDI_APPLICATION */), false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Remove();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        if (_ownsIcon && _hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
