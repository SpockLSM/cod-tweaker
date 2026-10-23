using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CoDTweaker.Services;

namespace CoDTweaker;

public partial class RestoreWindow : Window
{
    readonly List<BackupInfo> _backups;
    BackupInfo? _picked;

    public RestoreWindow(List<BackupInfo> backups)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        _backups = backups;
        List.ItemsSource = backups;
        if (backups.Count > 0) List.SelectedIndex = 0;
        long total = backups.Sum(b => b.Size);
        SummaryText.Text = $"{backups.Count} backup{(backups.Count == 1 ? "" : "s")} of this file, {(total >= 1024 * 1024 ? $"{total / 1048576.0:N1} MB" : $"{total / 1024.0:N0} KB")} in all. " +
                           $"The newest {BackupService.Keep} are kept; older ones are removed when a new backup is made.";
    }

    public BackupInfo? PickOne() => ShowDialog() == true ? _picked : null;

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        _picked = List.SelectedItem as BackupInfo;
        if (_picked == null) return;
        DialogResult = true;
    }

    void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Restore_Click(sender, e);

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = _backups.Count > 0 ? Path.GetDirectoryName(_backups[0].Path)! : BackupService.Root;
        if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
}
