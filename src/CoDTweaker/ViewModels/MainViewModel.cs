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
    readonly PinStore _pins;
    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    SettingsDocument? _doc;
    bool _loading;

    public ObservableCollection<FileItemVm> Files { get; } = new();
    public ObservableCollection<SectionVm> Sections { get; } = new();
    public AppSettings Prefs => _prefs;
    public PinStore Pins => _pins;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ReloadKeepEditsCommand { get; }
    public RelayCommand ShowDiskChangesCommand { get; }
    public RelayCommand ToggleLockCommand { get; }
    public RelayCommand LockAllCommand { get; }
    public RelayCommand UnlockAllCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand ExportPresetCommand { get; }
    public RelayCommand ExportPinnedPresetCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }
    public RelayCommand ApplyPinsNowCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DiscardAllCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand OpenUpdateCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }

    // The window plugs these in so the view model can ask questions without knowing about WPF.
    public Func<string, string, bool>? Confirm { get; set; }
    public Action<string, string>? Alert { get; set; }
    public Func<string?>? PickFolder { get; set; }
    public Func<string?>? PickFile { get; set; }
    public Func<string?>? PickPresetFile { get; set; }
    public Func<string, string?>? PickSavePath { get; set; }
    public Func<List<BackupInfo>, BackupInfo?>? PickBackup { get; set; }
    public Action<CompareViewModel>? ShowCompare { get; set; }

    /// <summary>Raised after the Guard wrote pinned values back, with a one-line account for a tray notice.</summary>
    public event Action<string>? GuardApplied;

    /// <summary>How long after the game exits the Guard waits for it to finish writing its files.</summary>
    public static TimeSpan GuardDelay { get; set; } = TimeSpan.FromSeconds(4);

    public MainViewModel(string? playersFolder = null, AppSettings? prefs = null, PinStore? pins = null)
    {
        _prefs = prefs ?? AppSettings.Load();
        _pins = pins ?? PinStore.Load();
        _playersFolder = playersFolder ??
            (_prefs.PlayersFolder is { Length: > 0 } p && Directory.Exists(p) ? p : FileLocator.DefaultPlayersFolder);

        SaveCommand = new RelayCommand(Save, () => _doc != null && ChangeCount > 0);
        ReloadCommand = new RelayCommand(() => Reload(), () => _doc != null);
        ReloadKeepEditsCommand = new RelayCommand(() => ReloadKeepingEdits(), () => _doc != null);
        ShowDiskChangesCommand = new RelayCommand(ShowDiskChanges, () => _doc != null);
        ToggleLockCommand = new RelayCommand(ToggleLock, () => _doc != null);
        LockAllCommand = new RelayCommand(() => SetLockAll(true), () => Files.Any(f => f.Info.IsGameFile));
        UnlockAllCommand = new RelayCommand(() => SetLockAll(false), () => Files.Any(f => f.Info.IsGameFile));
        RestoreCommand = new RelayCommand(Restore, () => _doc != null);
        CompareCommand = new RelayCommand(Compare, () => _doc != null);
        ExportPresetCommand = new RelayCommand(() => ExportPreset(pinnedOnly: false), () => _doc != null);
        ExportPinnedPresetCommand = new RelayCommand(() => ExportPreset(pinnedOnly: true), () => _doc != null && PinnedCount > 0);
        ApplyPresetCommand = new RelayCommand(ApplyPreset, () => _doc != null);
        ApplyPinsNowCommand = new RelayCommand(ApplyPinsNow, () => Sections.SelectMany(s => s.All).Any(v => v.PinMismatch));
        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenBackupsFolderCommand = new RelayCommand(OpenBackupsFolder);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        OpenFileCommand = new RelayCommand(OpenFile);
        RefreshCommand = new RelayCommand(RefreshFiles);
        DiscardAllCommand = new RelayCommand(DiscardAll, () => ChangeCount > 0);
        CheckUpdatesCommand = new RelayCommand(() => CheckForUpdates(manual: true));
        OpenUpdateCommand = new RelayCommand(() => { if (UpdateAvailable != null) OpenUrl(UpdateAvailable.Url); });
        DismissUpdateCommand = new RelayCommand(() => { if (UpdateAvailable != null) { _prefs.DismissedUpdate = UpdateAvailable.Tag; _prefs.Save(); UpdateAvailable = null; } });

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

    bool _onlyPinned;
    public bool OnlyPinned { get => _onlyPinned; set { if (SetProperty(ref _onlyPinned, value)) ApplyFilter(); } }

    bool _onlyHidden;
    /// <summary>Show only settings the in-game menu has no control for.</summary>
    public bool OnlyHidden { get => _onlyHidden; set { if (SetProperty(ref _onlyHidden, value)) ApplyFilter(); } }

    public bool LockAfterSave
    {
        get => _prefs.LockAfterSave;
        set { if (_prefs.LockAfterSave != value) { _prefs.LockAfterSave = value; _prefs.Save(); OnPropertyChanged(); } }
    }

    public bool GuardPinned
    {
        get => _prefs.GuardPinned;
        set { if (_prefs.GuardPinned != value) { _prefs.GuardPinned = value; _prefs.Save(); OnPropertyChanged(); OnPropertyChanged(nameof(GuardText)); } }
    }

    public string GuardText => !GuardPinned ? ""
        : PinnedCount == 0 && !_pins.PinnedFiles.Any() ? "Guard is on but nothing is pinned yet. Pin a setting with the pin button on its row."
        : $"Guard is on: {_pins.PinnedFiles.Count()} file(s) with pins.";

    bool _isLocked;
    public bool IsLocked { get => _isLocked; private set { if (SetProperty(ref _isLocked, value)) { OnPropertyChanged(nameof(LockText)); OnPropertyChanged(nameof(LockTip)); } } }
    public string LockText => _doc == null ? "" : IsLocked ? "Locked (read-only)" : "Unlocked";
    public string LockTip => IsLocked
        ? "The file is read-only. The game can't overwrite your edits, but its settings menu can't save either."
        : "The file is writable. The game will overwrite it with its own values when it exits.";

    bool _isGameRunning;
    public bool IsGameRunning { get => _isGameRunning; private set => SetProperty(ref _isGameRunning, value); }

    bool _isStale;
    /// <summary>The file on disk no longer matches what was loaded: the game (or something else) wrote it.</summary>
    public bool IsStale { get => _isStale; private set => SetProperty(ref _isStale, value); }

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

    int _pinnedCount;
    public int PinnedCount
    {
        get => _pinnedCount;
        private set
        {
            if (!SetProperty(ref _pinnedCount, value)) return;
            OnPropertyChanged(nameof(PinnedText)); OnPropertyChanged(nameof(GuardText));
            ExportPinnedPresetCommand.RaiseCanExecuteChanged(); ApplyPinsNowCommand.RaiseCanExecuteChanged();
        }
    }
    public string PinnedText => PinnedCount == 0 ? "" : PinnedCount == 1 ? "1 pinned" : $"{PinnedCount} pinned";

    string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    ReleaseInfo? _update;
    public ReleaseInfo? UpdateAvailable { get => _update; private set { if (SetProperty(ref _update, value)) { OnPropertyChanged(nameof(HasUpdate)); OnPropertyChanged(nameof(UpdateText)); } } }
    public bool HasUpdate => _update != null;
    public string UpdateText => _update == null ? "" : $"CoD Tweaker {_update.Tag} is available (you have {UpdateCheck.Current.ToString(3)}).";

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
        LockAllCommand.RaiseCanExecuteChanged(); UnlockAllCommand.RaiseCanExecuteChanged();
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

    void OpenBackupsFolder()
    {
        try { Directory.CreateDirectory(BackupService.Root); } catch { }
        if (Directory.Exists(BackupService.Root)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.Root}\"") { UseShellExecute = true });
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ----- the document -----

    void LoadSelected()
    {
        foreach (var s in Sections) foreach (var v in s.All) v.PropertyChanged -= OnSettingChanged;
        Sections.Clear();
        _doc = null;
        IsStale = false;
        if (SelectedFile != null)
        {
            try
            {
                _doc = SettingsDocument.Load(SelectedFile.Path);
                _loading = true;
                var pins = _pins.For(_doc.Path);
                foreach (var section in _doc.Sections)
                {
                    var vm = new SectionVm(section);
                    foreach (var s in vm.All)
                    {
                        var pin = pins.FirstOrDefault(p => p.Variant == s.Model.Variant && p.Name == s.Model.Name);
                        if (pin != null) { s.PinnedValue = pin.Value; s.IsPinned = true; }
                        s.PropertyChanged += OnSettingChanged;
                    }
                    Sections.Add(vm);
                }
                _loading = false;
                Status = $"{_doc.AllSettings.Count()} settings in {SelectedFile.FileName}";
            }
            catch (Exception ex)
            {
                _loading = false;
                Status = $"Couldn't read {SelectedFile.FileName}: {ex.Message}";
            }
        }
        RefreshLockState();
        ChangeCount = 0;
        PinnedCount = Sections.Sum(s => s.All.Count(v => v.IsPinned));
        ApplyFilter();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(LockText));
        foreach (var c in new[] { ReloadCommand, ReloadKeepEditsCommand, ShowDiskChangesCommand, ToggleLockCommand, RestoreCommand, CompareCommand, ExportPresetCommand, ExportPinnedPresetCommand, ApplyPresetCommand, ApplyPinsNowCommand })
            c.RaiseCanExecuteChanged();
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SettingVm vm || _doc == null) return;
        switch (e.PropertyName)
        {
            case nameof(SettingVm.IsModified):
                ChangeCount = Sections.Sum(s => s.All.Count(v => v.IsModified));
                if (OnlyModified) ApplyFilter();
                break;
            case nameof(SettingVm.IsPinned):
                if (_loading) return;
                if (vm.IsPinned) { _pins.Set(_doc.Path, vm.Model.Name, vm.Model.Variant, vm.Value); vm.PinnedValue = vm.Value; }
                else { _pins.Remove(_doc.Path, vm.Model.Name, vm.Model.Variant); vm.PinnedValue = null; }
                _pins.Save();
                PinnedCount = Sections.Sum(s => s.All.Count(v => v.IsPinned));
                if (OnlyPinned) ApplyFilter();
                break;
            case nameof(SettingVm.Value):
                // A pinned setting the user edits is pinned at the new value; the Guard restores what you last chose.
                if (_loading || !vm.IsPinned || vm.PinnedValue == vm.Value) return;
                _pins.Set(_doc.Path, vm.Model.Name, vm.Model.Variant, vm.Value);
                vm.PinnedValue = vm.Value;
                _pins.Save();
                ApplyPinsNowCommand.RaiseCanExecuteChanged();
                break;
        }
    }

    void ApplyFilter()
    {
        foreach (var s in Sections) s.Filter(Search.Trim(), OnlyModified, OnlyPinned, OnlyHidden);
    }

    public void RefreshLockState()
    {
        foreach (var f in Files) f.RefreshLock();
        IsLocked = SelectedFile?.IsLocked ?? false;
    }

    /// <summary>Called every few seconds by the window: game state, lock state, disk changes and the Guard.</summary>
    public void Tick()
    {
        RefreshGameState();
        RefreshLockState();
        CheckDisk();
        if (_guardDueAt != null && DateTime.UtcNow >= _guardDueAt) { _guardDueAt = null; RunGuard(); }
    }

    public void CheckDisk()
    {
        if (_doc == null) { IsStale = false; return; }
        IsStale = _doc.IsStaleOnDisk();
    }

    bool Reload(bool ask = true)
    {
        if (ask && ChangeCount > 0 && !(Confirm?.Invoke("Discard changes?", $"Throw away {ChangeCount} unsaved change(s) and reload from disk?") ?? true)) return false;
        LoadSelected();
        return true;
    }

    /// <summary>Reloads the file from disk and puts the unsaved edits back on top. Returns how many edits could not be kept.</summary>
    public int ReloadKeepingEdits()
    {
        var edits = Sections.SelectMany(s => s.All).Where(v => v.IsModified)
            .Select(v => (v.Model.Key, v.Model.Name, v.Model.Variant, v.Value)).ToList();
        LoadSelected();
        int lost = 0;
        if (_doc != null)
        {
            var byModel = Sections.SelectMany(s => s.All).ToDictionary(v => v.Model);
            foreach (var (key, name, variant, value) in edits)
            {
                var s = _doc.Match(name, variant, key);
                if (s != null && byModel.TryGetValue(s, out var vm) && vm.IsEditable) vm.Value = value; else lost++;
            }
        }
        int kept = edits.Count - lost;
        Status = edits.Count == 0 ? "Reloaded from disk."
            : lost == 0 ? $"Reloaded from disk; your {kept} edit(s) are still pending."
            : $"Reloaded from disk; {kept} edit(s) kept, {lost} could not be placed in the new file.";
        return lost;
    }

    void DiscardAll()
    {
        foreach (var s in Sections) foreach (var v in s.All) v.Reset();
    }

    // ----- the game and the Guard -----

    bool _gameKnown;
    DateTime? _guardDueAt;
    readonly HashSet<string> _relock = new(StringComparer.OrdinalIgnoreCase);

    public void RefreshGameState()
    {
        bool running = GameProcess.IsRunning();
        if (_gameKnown && running != IsGameRunning)
        {
            if (running) OnGameStarted(); else OnGameExited();
        }
        _gameKnown = true;
        IsGameRunning = running;
    }

    void OnGameStarted()
    {
        if (!GuardPinned) return;
        int unlocked = 0;
        foreach (var path in _pins.PinnedFiles.ToList())
        {
            if (!GuardService.IsLocked(path)) continue;
            try { GuardService.SetLock(path, false); _relock.Add(path); unlocked++; } catch { }
        }
        RefreshLockState();
        if (unlocked > 0) Status = $"Guard: unlocked {unlocked} file(s) while the game runs; pinned values go back when it exits.";
    }

    void OnGameExited()
    {
        if (!GuardPinned && _relock.Count == 0) return;
        _guardDueAt = DateTime.UtcNow + GuardDelay;
    }

    /// <summary>Writes pinned values back into every file that has pins, then restores the locks the Guard lifted.</summary>
    public string RunGuard()
    {
        var lines = new List<string>();
        int restored = 0, files = 0;
        foreach (var path in _pins.PinnedFiles.ToList())
        {
            var r = GuardService.Reapply(path, _pins.For(path), lockAfter: _relock.Contains(path) ? true : null);
            _relock.Remove(path);
            if (r.Error != null) { lines.Add($"{Path.GetFileName(path)}: {r.Error}"); continue; }
            files++;
            if (r.Restored > 0) { restored += r.Restored; lines.Add($"{r.Restored} value(s) in {Path.GetFileName(path)}"); }
            if (r.Missing.Count > 0) lines.Add($"{r.Missing.Count} pin(s) not found in {Path.GetFileName(path)}: {string.Join(", ", r.Missing)}");
        }
        foreach (var path in _relock.ToList()) { try { GuardService.SetLock(path, true); } catch { } }
        _relock.Clear();

        string message = restored == 0
            ? (lines.Count == 0 ? $"Guard: all pinned values were already in place ({files} file(s) checked)." : "Guard: " + string.Join("; ", lines))
            : "Guard: put back " + string.Join("; ", lines) + ".";
        Status = message;

        if (_doc != null)
        {
            if (ChangeCount == 0 && _doc.IsStaleOnDisk()) LoadSelected(); else CheckDisk();
        }
        RefreshLockState();
        if (restored > 0 || lines.Count > 0) GuardApplied?.Invoke(message);
        return message;
    }

    /// <summary>Sets every pinned setting in the open file back to its pinned value, as pending edits.</summary>
    void ApplyPinsNow()
    {
        int n = 0;
        foreach (var v in Sections.SelectMany(s => s.All).Where(v => v.PinMismatch).ToList()) { v.Value = v.PinnedValue!; n++; }
        Status = n == 0 ? "All pinned values are already in place." : $"{n} pinned value(s) put back. Save to write them.";
    }

    // ----- writing -----

    void Save()
    {
        if (_doc == null) return;
        RefreshGameState();
        if (IsGameRunning && !(Confirm?.Invoke("Call of Duty is running",
                "The game only reads this file when it starts, and overwrites it when it exits unless the file is locked.\n\nSave anyway?") ?? true))
            return;

        if (_doc.IsStaleOnDisk())
        {
            bool reload = Confirm?.Invoke("The file changed on disk",
                "Call of Duty (or something else) wrote this file after you opened it. Saving now would throw those changes away.\n\n" +
                "Reload the file, keep your edits on top, and then save?") ?? true;
            if (!reload) { Status = "Save cancelled. Use Reload, or 'Show what changed', to see the new file."; return; }
            ReloadKeepingEdits();
            if (_doc == null || ChangeCount == 0) { Status = "Reloaded; nothing left to save."; return; }
        }

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

    void SetLockAll(bool locked)
    {
        int n = 0;
        foreach (var f in Files.Where(f => f.Info.IsGameFile))
        {
            try { SetReadOnly(f.Path, locked); n++; } catch { }
        }
        RefreshLockState();
        Status = locked ? $"Locked {n} file(s). The game can't overwrite them." : $"Unlocked {n} file(s). The game can save to them again.";
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

    // ----- compare and presets -----

    CompareViewModel NewCompare()
    {
        var cvm = new CompareViewModel(_doc!, Sections.SelectMany(s => s.All), BackupService.List(_doc!.Path))
        {
            PickFile = PickFile, PickPreset = PickPresetFile, Alert = Alert,
        };
        return cvm;
    }

    void Compare()
    {
        if (_doc == null) return;
        var cvm = NewCompare();
        if (cvm.Backups.Count > 0) cvm.SelectedBackup = cvm.Backups[0];
        ShowCompare?.Invoke(cvm);
    }

    void ShowDiskChanges()
    {
        if (_doc == null) return;
        var cvm = NewCompare();
        cvm.UseDisk();
        ShowCompare?.Invoke(cvm);
    }

    public Preset BuildPreset(bool pinnedOnly)
    {
        var info = SelectedFile!.Info;
        var values = Sections.SelectMany(s => s.All)
            .Where(v => v.IsEditable && (!pinnedOnly || v.IsPinned))
            .Select(v => (v.Model, v.Value));
        return Preset.From(info.Game, info.Kind, info.Title, values);
    }

    void ExportPreset(bool pinnedOnly)
    {
        if (_doc == null || SelectedFile == null) return;
        var preset = BuildPreset(pinnedOnly);
        string stem = SelectedFile.Info.Game.Length > 0 ? SelectedFile.Info.Game : "cod";
        string suggested = $"{stem}-{SelectedFile.Info.Kind}{(pinnedOnly ? "-pinned" : "")}-preset.json";
        var path = PickSavePath?.Invoke(suggested);
        if (path == null) return;
        try
        {
            preset.Save(path);
            Status = $"Exported {preset.Values.Count} value(s) to {path}";
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't write the preset", ex.Message); }
    }

    void ApplyPreset()
    {
        if (_doc == null) return;
        var path = PickPresetFile?.Invoke();
        if (path == null) return;
        var cvm = NewCompare();
        cvm.UsePresetFile(path);
        ShowCompare?.Invoke(cvm);
    }

    // ----- updates -----

    public void CheckForUpdates(bool manual)
    {
        if (!manual && !_prefs.CheckForUpdates) return;
        _ = Task.Run(async () =>
        {
            var latest = await UpdateCheck.LatestAsync();
            void Apply()
            {
                if (latest == null) { if (manual) Status = "Couldn't check for updates (offline, or no public release yet)."; return; }
                if (!UpdateCheck.IsNewer(latest)) { if (manual) Status = $"You have the latest version ({UpdateCheck.Current.ToString(3)})."; return; }
                if (!manual && latest.Tag == _prefs.DismissedUpdate) return;
                UpdateAvailable = latest;
                if (manual) Status = UpdateText;
            }
            if (_ui != null) _ui.Post(_ => Apply(), null); else Apply();
        });
    }

    static void SetReadOnly(string path, bool readOnly) => GuardService.SetLock(path, readOnly);
}
