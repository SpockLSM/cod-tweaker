using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CoDTweaker.Services;
using CoDTweaker.ViewModels;
using Microsoft.Win32;

namespace CoDTweaker;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new(App.PlayersFolderOverride);
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public ICollectionView FilesView { get; }

    public MainWindow()
    {
        InitializeComponent();

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
        _vm.PickBackup = backups => new RestoreWindow(backups) { Owner = this }.PickOne();

        DataContext = _vm;
        FileList.ItemsSource = FilesView;

        _timer.Tick += (_, _) => { _vm.RefreshGameState(); _vm.RefreshLockState(); };
        _timer.Start();
        _vm.RefreshGameState();

        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _vm.SaveCommand.CanExecute(null)) { _vm.SaveCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        };
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.ChangeCount == 0) return;
        var r = MessageBox.Show(this, $"You have {_vm.ChangeCount} unsaved change(s). Save before closing?", "Unsaved changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (r == MessageBoxResult.Yes) { _vm.SaveCommand.Execute(null); if (_vm.ChangeCount > 0) e.Cancel = true; }
    }

    // Pressing Enter in a value box applies it right away instead of waiting for focus to leave.
    void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
