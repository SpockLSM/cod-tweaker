using System.IO;
using CoDTweaker.Services;
using Spock.AppKit.Mvvm;

namespace CoDTweaker.ViewModels;

public sealed class FileItemVm : ObservableObject
{
    public SettingsFileInfo Info { get; }
    public string Path => Info.Path;
    public string Title => Info.Title;
    public string Subtitle => Info.Subtitle;
    public string Group => Info.Group;
    public string FileName => System.IO.Path.GetFileName(Info.Path);

    bool _isLocked;
    public bool IsLocked { get => _isLocked; private set => SetProperty(ref _isLocked, value); }

    public FileItemVm(SettingsFileInfo info) { Info = info; RefreshLock(); }

    public void RefreshLock()
    {
        try { IsLocked = File.Exists(Path) && (File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0; }
        catch { IsLocked = false; }
    }
}
