using System.Collections.ObjectModel;
using System.IO;
using CoDTweaker.Models;
using CoDTweaker.Services;

namespace CoDTweaker.ViewModels;

/// <summary>One setting whose value differs between this file and the other side.</summary>
public sealed class DiffRow : ObservableObject
{
    public SettingVm Mine { get; }
    public string Theirs { get; }
    public string Section { get; }
    public RelayCommand TakeCommand { get; }

    public DiffRow(SettingVm mine, string theirs, string section)
    {
        Mine = mine; Theirs = theirs; Section = section;
        TakeCommand = new RelayCommand(Take, () => IsDifferent && Mine.IsEditable);
        Mine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SettingVm.Value)) return;
            OnPropertyChanged(nameof(IsDifferent)); OnPropertyChanged(nameof(TakeText)); TakeCommand.RaiseCanExecuteChanged();
        };
    }

    public string Name => Mine.DisplayName;
    public bool IsDifferent => Mine.Value != Theirs;
    public string TakeText => !Mine.IsEditable ? "Not editable" : IsDifferent ? "Take" : "✓ Same";

    /// <summary>Adopts the other side's value as a pending change in the main window.</summary>
    public void Take() { if (Mine.IsEditable) Mine.Value = Theirs; }
}

/// <summary>
/// Compares the file open in the main window (with any unsaved edits) against a backup, another file,
/// a preset, or the file as it is on disk right now. "Take" copies a value across as a pending change.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
    readonly SettingsDocument _doc;
    readonly Dictionary<Setting, SettingVm> _vms;

    public List<BackupInfo> Backups { get; }
    public ObservableCollection<DiffRow> Rows { get; } = new();
    public RelayCommand PickFileCommand { get; }
    public RelayCommand PickPresetCommand { get; }
    public RelayCommand TakeAllCommand { get; }

    public Func<string?>? PickFile { get; set; }
    public Func<string?>? PickPreset { get; set; }
    public Action<string, string>? Alert { get; set; }

    public CompareViewModel(SettingsDocument doc, IEnumerable<SettingVm> settings, List<BackupInfo> backups)
    {
        _doc = doc;
        _vms = settings.ToDictionary(v => v.Model);
        Backups = backups;
        PickFileCommand = new RelayCommand(() => { var p = PickFile?.Invoke(); if (p != null) UseFile(p); });
        PickPresetCommand = new RelayCommand(() => { var p = PickPreset?.Invoke(); if (p != null) UsePresetFile(p); });
        TakeAllCommand = new RelayCommand(() => { foreach (var r in Rows) r.Take(); }, () => Rows.Any(r => r.IsDifferent && r.Mine.IsEditable));
    }

    BackupInfo? _selectedBackup;
    public BackupInfo? SelectedBackup
    {
        get => _selectedBackup;
        set { if (SetProperty(ref _selectedBackup, value) && value != null) UseFile(value.Path, $"the backup from {value.Time:g}"); }
    }

    string _sourceName = "";
    public string SourceName { get => _sourceName; private set => SetProperty(ref _sourceName, value); }

    string _summary = "Pick a backup, another file or a preset to compare with.";
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public bool HasRows => Rows.Count > 0;

    public void UseFile(string path, string? name = null)
    {
        try
        {
            var other = SettingsDocument.Load(path);
            Show(Diff.Refs(other), name ?? Path.GetFileName(path), other.AllSettings.Count());
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't read the file", ex.Message); }
    }

    public void UsePresetFile(string path)
    {
        try
        {
            var preset = Preset.Load(path);
            string made = preset.Title.Length > 0 ? $"the preset {Path.GetFileName(path)} (made from {preset.Title})" : $"the preset {Path.GetFileName(path)}";
            Show(Diff.Refs(preset), made, preset.Values.Count);
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't read the preset", ex.Message); }
    }

    public void UsePreset(Preset preset, string name) => Show(Diff.Refs(preset), name, preset.Values.Count);

    /// <summary>The file as it is on disk right now, against what is loaded (plus edits) in the window.</summary>
    public void UseDisk()
    {
        try
        {
            var disk = SettingsDocument.Load(_doc.Path);
            Show(Diff.Refs(disk), "the file as it is on disk now", disk.AllSettings.Count());
        }
        catch (Exception ex) { Alert?.Invoke("Couldn't read the file", ex.Message); }
    }

    void Show(IEnumerable<ValueRef> theirs, string name, int theirCount)
    {
        SourceName = name;
        var result = Diff.Pair(_doc, theirs);
        Rows.Clear();
        foreach (var (mine, value) in result.Pairs)
            if (_vms.TryGetValue(mine, out var vm) && vm.Value != value)
                Rows.Add(new DiffRow(vm, value, SectionOf(mine)));
        int same = result.Pairs.Count - Rows.Count;
        var parts = new List<string> { $"{Rows.Count} difference{(Rows.Count == 1 ? "" : "s")} between this file and {name}" };
        if (same > 0) parts.Add($"{same} matching");
        if (result.OnlyInOther.Count > 0) parts.Add($"{result.OnlyInOther.Count} only in the other side");
        if (result.OnlyInMine > 0) parts.Add($"{result.OnlyInMine} only in this file");
        Summary = string.Join("; ", parts) + ".";
        if (theirCount == 0) Summary = $"{name} holds no settings.";
        OnPropertyChanged(nameof(HasRows));
        TakeAllCommand.RaiseCanExecuteChanged();
    }

    string SectionOf(Setting s) => _doc.Sections.FirstOrDefault(sec => sec.Settings.Contains(s))?.Name ?? "";
}
