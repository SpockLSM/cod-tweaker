using System.IO;
using System.Text;
using CoDTweaker.Services;
using CoDTweaker.ViewModels;
using Xunit;

namespace CoDTweaker.Tests;

/// <summary>Drives the view model the way the window does, against a throwaway players folder.</summary>
public class SaveFlowTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "CoDTweakerTests", Guid.NewGuid().ToString("N"));
    readonly string _players;
    readonly string _systemFile;
    readonly byte[] _original;

    public SaveFlowTests()
    {
        _players = Path.Combine(_root, "players");
        Directory.CreateDirectory(Path.Combine(_players, "1234567890123456"));
        _original = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.txt")).Replace("\r\n", "\n"));
        _systemFile = Path.Combine(_players, "s.1.0.cod26.txt");
        File.WriteAllBytes(_systemFile, _original);
        File.WriteAllBytes(Path.Combine(_players, "1234567890123456", "g.p.cod26.1.0.l.txt"), _original);
        File.WriteAllBytes(Path.Combine(_players, "s.1.0.cod26.txt_bak.txt"), _original); // a user's own backup: must not be listed
        BackupService.RootOverride = Path.Combine(_root, "backups");
        AppPaths.RootOverride = Path.Combine(_root, "app");
    }

    public void Dispose()
    {
        BackupService.RootOverride = null;
        AppPaths.RootOverride = null;
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    MainViewModel NewVm(bool lockAfterSave) => new(_players, new AppSettings { LockAfterSave = lockAfterSave });

    [Fact]
    public void Finds_the_known_files_and_lists_copies_last()
    {
        var vm = NewVm(true);
        Assert.Equal(new[] { "System settings (cod26)", "Player profile (cod26)", "s.1.0.cod26.txt_bak.txt" }, vm.Files.Select(f => f.Title));
        Assert.Equal("Player 1234567890123456", vm.Files[1].Group);
        Assert.Equal("Other copies", vm.Files[2].Group);
        Assert.Same(vm.Files[0], vm.SelectedFile);
        Assert.True(vm.HasDocument);
    }

    [Fact]
    public void Save_changes_only_the_edited_value_backs_up_and_locks()
    {
        var vm = NewVm(lockAfterSave: true);
        var fps = vm.Sections.SelectMany(s => s.All).Single(s => s.Model.Name == "MaxFpsInGame");
        fps.NumberValue = 158;
        Assert.Equal(1, vm.ChangeCount);
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);

        var expected = Encoding.UTF8.GetString(_original).Replace("MaxFpsInGame@0;13305;58861 = 100 //", "MaxFpsInGame@0;13305;58861 = 158 //");
        Assert.Equal(expected, Encoding.UTF8.GetString(File.ReadAllBytes(_systemFile)));
        Assert.True((File.GetAttributes(_systemFile) & FileAttributes.ReadOnly) != 0);
        Assert.True(vm.IsLocked);
        Assert.Equal(0, vm.ChangeCount);

        var backups = BackupService.List(_systemFile);
        Assert.Single(backups);
        Assert.Equal(_original, File.ReadAllBytes(backups[0].Path));
    }

    [Fact]
    public void Save_on_a_locked_file_works_and_can_leave_it_unlocked()
    {
        File.SetAttributes(_systemFile, FileAttributes.ReadOnly);
        var vm = NewVm(lockAfterSave: false);
        Assert.True(vm.IsLocked);
        var ptt = vm.Sections.SelectMany(s => s.All).Single(s => s.Model.Name == "VoicePushToTalk");
        ptt.BoolValue = false;

        vm.SaveCommand.Execute(null);

        Assert.Contains("VoicePushToTalk@0;57752;20945 = false", File.ReadAllText(_systemFile));
        Assert.False(vm.IsLocked);
        Assert.True((File.GetAttributes(_systemFile) & FileAttributes.ReadOnly) == 0);
    }

    [Fact]
    public void Restore_puts_a_backup_back_and_keeps_the_current_file_safe()
    {
        var vm = NewVm(lockAfterSave: true);
        vm.Sections.SelectMany(s => s.All).Single(s => s.Model.Name == "MaxFpsInGame").NumberValue = 158;
        vm.SaveCommand.Execute(null);
        var afterFirstSave = File.ReadAllBytes(_systemFile);

        vm.PickBackup = list => list[0];       // the pre-save copy
        vm.RestoreCommand.Execute(null);

        Assert.Equal(_original, File.ReadAllBytes(_systemFile));
        Assert.True(vm.IsLocked, "restoring keeps the lock as it was");
        var backups = BackupService.List(_systemFile);
        Assert.Equal(2, backups.Count);
        Assert.Contains(backups, b => File.ReadAllBytes(b.Path).SequenceEqual(afterFirstSave));
    }

    [Fact]
    public void Switching_files_with_changes_asks_first()
    {
        var vm = NewVm(true);
        vm.Sections[0].All[0].Value = "0.250000";
        bool asked = false;
        vm.Confirm = (_, _) => { asked = true; return false; };
        vm.SelectedFile = vm.Files[1];
        Assert.True(asked);
        Assert.Same(vm.Files[0], vm.SelectedFile);
        Assert.Equal(1, vm.ChangeCount);
    }
}
