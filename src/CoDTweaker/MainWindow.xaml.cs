using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CoDTweaker.ViewModels;
using Microsoft.Win32;
using Spock.AppKit;

namespace CoDTweaker;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new(App.PlayersFolderOverride);
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly TrayIcon _tray;
    bool _exiting;

    public ICollectionView FilesView { get; }

    public MainWindow()
    {
        InitializeComponent();
        WindowPlacement.Restore(this, _vm.Prefs.WindowWidth, _vm.Prefs.WindowHeight, _vm.Prefs.WindowMaximized);

        FilesView = CollectionViewSource.GetDefaultView(_vm.Files);
        FilesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FileItemVm.Group)));

        _vm.Confirm = (title, text) => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        _vm.Alert = (title, text) => MessageBox.Show(this, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        _vm.PickFolder = () =>
        {
            var dlg = new OpenFolderDialog { Title = "Choose Call of Duty's 'players' folder", InitialDirectory = _vm.PlayersFolder };
            return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
        };
        _vm.PickFile = () =>
        {
            var dlg = new OpenFileDialog { Title = "Open a settings file", Filter = "Settings files (*.txt)|*.txt|All files|*.*", InitialDirectory = _vm.PlayersFolder };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        _vm.PickPresetFile = () =>
        {
            var dlg = new OpenFileDialog { Title = "Open a preset", Filter = "CoD Tweaker presets (*.json)|*.json|All files|*.*" };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        _vm.PickSavePath = suggested =>
        {
            var dlg = new SaveFileDialog { Title = "Save the preset", Filter = "CoD Tweaker presets (*.json)|*.json", FileName = suggested, DefaultExt = ".json" };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        _vm.PickBackup = backups => new RestoreWindow(backups) { Owner = this }.PickOne();
        _vm.ShowCompare = cvm => new CompareWindow(cvm) { Owner = this }.ShowDialog();

        _tray = new TrayIcon("CoD Tweaker") { ContextMenu = BuildTrayMenu(), Visible = _vm.GuardPinned };
        _tray.Activated += ShowFromTray;
        _vm.GuardApplied += message => { if (!IsVisible) _tray.Notify("CoD Tweaker", message); };
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.GuardPinned)) _tray.Visible = _vm.GuardPinned || !IsVisible; };

        DataContext = _vm;
        FileList.ItemsSource = FilesView;
        UpdateThemeButton();
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);

        _timer.Tick += (_, _) => _vm.Tick();
        _timer.Start();
        _vm.RefreshGameState();
        _vm.CheckForUpdates(manual: false);

        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _vm.SaveCommand.CanExecute(null)) { _vm.SaveCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        };
    }

    // ----- tray -----

    ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open CoD Tweaker" };
        open.Click += (_, _) => ShowFromTray();
        var guard = new MenuItem { Header = "Guard pinned settings", IsCheckable = true };
        guard.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(MainViewModel.GuardPinned)) { Source = _vm, Mode = BindingMode.TwoWay });
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(open);
        menu.Items.Add(guard);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        return menu;
    }

    /// <summary>Closing the window keeps the app in the tray only while the Guard has something to do.</summary>
    public bool CanHideToTray => _vm.GuardPinned;

    public void HideToTray(bool firstTime)
    {
        Hide();
        _tray.Visible = true;
        if (firstTime && !_vm.Prefs.TrayHintShown)
        {
            _vm.Prefs.TrayHintShown = true;
            _vm.Prefs.Save();
            _tray.Notify("CoD Tweaker is still running", "It stays here to put your pinned settings back when the game exits. Right-click the icon to open it again or to exit.");
        }
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _tray.Visible = _vm.GuardPinned;
    }

    public void ExitApp()
    {
        _exiting = true;
        if (!IsVisible) Show(); // so the unsaved-changes question has an owner
        Close();
    }

    void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && CanHideToTray)
        {
            e.Cancel = true;
            HideToTray(firstTime: true);
            return;
        }
        if (_vm.ChangeCount > 0)
        {
            var r = MessageBox.Show(this, $"You have {_vm.ChangeCount} unsaved change(s). Save before closing?", "Unsaved changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; _exiting = false; return; }
            if (r == MessageBoxResult.Yes) { _vm.SaveCommand.Execute(null); if (_vm.ChangeCount > 0) { e.Cancel = true; _exiting = false; return; } }
        }
        var (w, h, max) = WindowPlacement.Capture(this);
        _vm.Prefs.WindowWidth = w; _vm.Prefs.WindowHeight = h; _vm.Prefs.WindowMaximized = max;
        _vm.Prefs.Save();
        _timer.Stop();
        _tray.Dispose();
    }

    // ----- small handlers -----

    void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        Theme.Apply(!Theme.IsDark);
        _vm.Prefs.Theme = Theme.IsDark ? "dark" : "light";
        _vm.Prefs.Save();
        UpdateThemeButton();
    }

    void UpdateThemeButton() => ThemeButton.Content = Theme.ToggleLabel;

    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu!;
        menu.DataContext = _vm;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    // Pressing Enter in a value box applies it right away instead of waiting for focus to leave.
    void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
