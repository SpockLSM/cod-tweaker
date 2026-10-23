using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CoDTweaker.Models;
using CoDTweaker.Services;

namespace CoDTweaker.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    readonly AppSettings _prefs;
    SettingsDocument? _doc;

    public ObservableCollection<FileItemVm> Files { get; } = new();
    public ObservableCollection<SectionVm> Sections { get; } = new();
    public AppSettings Prefs => _prefs;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ToggleLockCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DiscardAllCommand { get; }

    // The window plugs these in so the view model can ask questions without knowing about WPF.
    public Func<string, string, bool>? Confirm { get; set; }
    public Action<string, string>? Alert { get; set; }
    public Func<string?>? PickFolder { get; set; }
    public Func<string?>? PickFile { get; set; }
    public Func<List<BackupInfo>, BackupInfo?>? PickBackup { get; set; }

    public MainViewModel(string? playersFolder = null, AppSettings? prefs = null)
    {
        _prefs = prefs ?? AppSettings.Load();
        _playersFolder = playersFolder ??
            (_prefs.PlayersFolder is { Length: > 0 } p && Directory.Exists(p) ? p : FileLocator.DefaultPlayersFolder);

        SaveCommand = new RelayCommand(Save, () => _doc != null && ChangeCount > 0);
        ReloadCommand = new RelayCommand(() => Reload(), () => _doc != null);
        ToggleLockCommand = new RelayCommand(ToggleLock, () => _doc != null);
        RestoreCommand = new RelayCommand(Restore, () => _doc != null);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        OpenFileCommand = new RelayCommand(OpenFile);
        RefreshCommand = new RelayCommand(RefreshFiles);
        DiscardAllCommand = new RelayCommand(DiscardAll, () => ChangeCount > 0);

        RefreshFiles();
    }

    // ----- state shown in the window -----

    string _playersFolder;
    public string PlayersFolder { get => _playersFolder; private set { if (SetProperty(ref _playersFolder, value)) OnPropertyChanged(nameof(PlayersFolderDisplay)); } }
    public string PlayersFolderDisplay => FileLocator.DisplayPath(PlayersFolder);

    FileItemVm? _selectedFile;
    public FileItemVm? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (_selectedFile == value) return;
            if (ChangeCount > 0 && !(Confirm?.Invoke("Discard changes?", $"You have {ChangeCount} unsaved change(s) in {_selectedFile?.FileName}. Switch files and lose them?") ?? true))
            {
                OnPropertyChanged(); // the list box has already moved; push it back
                return;
            }
            _selectedFile = value;
            OnPropertyChanged();
            LoadSelected();
        }
    }

    string _search = "";
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }

    bool _onlyModified;
    public bool OnlyModified { get => _onlyModified; set { if (SetProperty(ref _onlyModified, value)) ApplyFilter(); } }

    public bool LockAfterSave
    {
        get => _prefs.LockAfterSave;
        set { if (_prefs.LockAfterSave != value) { _prefs.LockAfterSave = value; _prefs.Save(); OnPropertyChanged(); } }
    }

    bool _isLocked;
    public bool IsLocked { get => _isLocked; private set { if (SetProperty(ref _isLocked, value)) { OnPropertyChanged(nameof(LockText)); OnPropertyChanged(nameof(LockTip)); } } }
    public string LockText => _doc == null ? "" : IsLocked ? "Locked (read-only)" : "Unlocked";
    public string LockTip => IsLocked
        ? "The file is read-only. The game can't overwrite your edits, but its settings menu can't save either."
        : "The file is writable. The game will overwrite it with its own values when it exits.";

    bool _isGameRunning;
    public bool IsGameRunning { get => _isGameRunning; private set => SetProperty(ref _isGameRunning, value); }

    int _changeCount;
    public int ChangeCount
    {
        get => _changeCount;
        private set
        {
            if (!SetProperty(ref _changeCount, value)) return;
            OnPropertyChanged(nameof(ChangeText));
            SaveCommand.RaiseCanExecuteChanged();
            DiscardAllCommand.RaiseCanExecuteChanged();
        }
    }
    public string ChangeText => ChangeCount == 0 ? "No unsaved changes" : ChangeCount == 1 ? "1 unsaved change" : $"{ChangeCount} unsaved changes";

    string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool HasDocument => _doc != null;
    public bool HasNoFiles => Files.Count == 0;
    public string CurrentPath => _doc?.Path ?? "";

    // ----- files -----

    public void RefreshFiles()
    {
        string? keep = SelectedFile?.Path;
        Files.Clear();
        foreach (var f in FileLocator.Find(PlayersFolder)) Files.Add(new FileItemVm(f));
        OnPropertyChanged(nameof(HasNoFiles));
        var again = Files.FirstOrDefault(f => f.Path.Equals(keep, StringComparison.OrdinalIgnoreCase));
        _selectedFile = again ?? Files.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedFile));
        LoadSelected();
        if (Files.Count == 0) Status = $"No settings files found in {PlayersFolderDisplay}. Start the game once, or choose the folder.";
    }

    void BrowseFolder()
    {
        var dir = PickFolder?.Invoke();
        if (dir == null) return;
        PlayersFolder = dir;
        _prefs.PlayersFolder = dir;
        _prefs.Save();
        RefreshFiles();
    }

    void OpenFile()
    {
        var path = PickFile?.Invoke();
        if (path == null) return;
        var existing = Files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) { existing = new FileItemVm(FileLocator.Describe(path)); Files.Add(existing); OnPropertyChanged(nameof(HasNoFiles)); }
        SelectedFile = existing;
    }

    void OpenFolder()
    {
        string target = _doc != null ? Path.GetDirectoryName(_doc.Path)! : PlayersFolder;
        if (Directory.Exists(target)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
    }

    // ----- the document -----

    void LoadSelected()
    {
        foreach (var s in Sections) foreach (var v in s.All) v.PropertyChanged -= OnSettingChanged;
        Sections.Clear();
        _doc = null;
        if (SelectedFile != null)
        {
            try
            {
                _doc = SettingsDocument.Load(SelectedFile.Path);
                foreach (var section in _doc.Sections)
                {
                    var vm = new SectionVm(section);
                    foreach (var s in vm.All) s.PropertyChanged += OnSettingChanged;
                    Sections.Add(vm);
                }
                Status = $"{_doc.AllSettings.Count()} settings in {SelectedFile.FileName}";
            }
            catch (Exception ex)
            {
                Status = $"Couldn't read {SelectedFile.FileName}: {ex.Message}";
            }
        }
        RefreshLockState();
        ChangeCount = 0;
        ApplyFilter();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(LockText));
        ReloadCommand.RaiseCanExecuteChanged();
        ToggleLockCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingVm.IsModified)) return;
        ChangeCount = Sections.Sum(s => s.All.Count(v => v.IsModified));
        if (OnlyModified) ApplyFilter();
    }

    void ApplyFilter()
    {
        foreach (var s in Sections) s.Filter(Search.Trim(), OnlyModified);
    }

    public void RefreshLockState()
    {
        SelectedFile?.RefreshLock();
        IsLocked = SelectedFile?.IsLocked ?? false;
    }

    public void RefreshGameState() => IsGameRunning = GameProcess.IsRunning();

    bool Reload(bool ask = true)
    {
        if (ask && ChangeCount > 0 && !(Confirm?.Invoke("Discard changes?", $"Throw away {ChangeCount} unsaved change(s) and reload from disk?") ?? true)) return false;
        LoadSelected();
        return true;
    }

    void DiscardAll()
    {
        foreach (var s in Sections) foreach (var v in s.All) v.Reset();
    }

    // ----- writing -----

    void Save()
    {
        if (_doc == null) return;
        RefreshGameState();
        if (IsGameRunning && !(Confirm?.Invoke("Call of Duty is running",
                "The game only reads this file when it starts, and overwrites it when it exits unless the file is locked.\n\nSave anyway?") ?? true))
            return;

        var changes = Sections.SelectMany(s => s.All).Where(v => v.IsModified).ToDictionary(v => v.Model, v => v.Value);
        try
        {
            string backup = BackupService.Create(_doc.Path);
            var bytes = _doc.RenderBytes(changes);
            SetReadOnly(_doc.Path, false);
            File.WriteAllBytes(_doc.Path, bytes);
            SetReadOnly(_doc.Path, LockAfterSave);
            int n = changes.Count;
            Reload(ask: false);
            Status = $"Saved {n} change(s)" + (LockAfterSave ? " and locked the file. " : ". ") + $"Backup: {backup}";
        }
        catch (Exception ex)
        {
            Alert?.Invoke("Couldn't save", ex.Message);
            Status = "Save failed: " + ex.Message;
        }
    }

    void ToggleLock()
    {
        if (_doc == null) return;
        try
        {
            SetReadOnly(_doc.Path, !IsLocked);
            RefreshLockState();
            Status = IsLocked ? "Locked. The game can't overwrite this file now." : "Unlocked. The game can save to this file again.";
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't change the lock", ex.Message); }
    }

    void Restore()
    {
        if (_doc == null) return;
        var backups = BackupService.List(_doc.Path);
        if (backups.Count == 0) { Alert?.Invoke("No backups yet", "A backup is made every time you save from this app."); return; }
        var pick = PickBackup?.Invoke(backups);
        if (pick == null) return;
        try
        {
            string safety = BackupService.Create(_doc.Path);
            bool wasLocked = IsLocked;
            SetReadOnly(_doc.Path, false);
            File.Copy(pick.Path, _doc.Path, overwrite: true);
            SetReadOnly(_doc.Path, wasLocked);
            Reload(ask: false);
            Status = $"Restored the backup from {pick.Time:g}. The file as it was is saved as {safety}";
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't restore", ex.Message); }
    }

    static void SetReadOnly(string path, bool readOnly)
    {
        var attrs = File.GetAttributes(path);
        var wanted = readOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
        if (wanted != attrs) File.SetAttributes(path, wanted);
    }
}
