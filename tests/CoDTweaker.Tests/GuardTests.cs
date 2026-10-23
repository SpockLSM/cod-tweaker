using System.IO;
using System.Text;
using CoDTweaker.Models;
using CoDTweaker.Services;
using CoDTweaker.ViewModels;
using Xunit;

namespace CoDTweaker.Tests;

/// <summary>Pins, the Guard, disk-change detection, lock all, backup pruning, notes and the command-line apply.</summary>
public class GuardTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "CoDTweakerTests", Guid.NewGuid().ToString("N"));
    readonly string _players;
    readonly string _systemFile;
    readonly string _profileFile;
    readonly byte[] _original;

    public GuardTests()
    {
        _players = Path.Combine(_root, "players");
        Directory.CreateDirectory(Path.Combine(_players, "1234567890123456"));
        _original = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.txt")).Replace("\r\n", "\n"));
        _systemFile = Path.Combine(_players, "s.1.0.cod26.txt");
        _profileFile = Path.Combine(_players, "1234567890123456", "g.p.cod26.1.0.l.txt");
        File.WriteAllBytes(_systemFile, _original);
        File.WriteAllBytes(_profileFile, _original);
        AppPaths.RootOverride = Path.Combine(_root, "app");
        BackupService.RootOverride = Path.Combine(_root, "backups");
        MainViewModel.GuardDelay = TimeSpan.Zero;
    }

    public void Dispose()
    {
        GameProcess.Override = null;
        AppPaths.RootOverride = null;
        BackupService.RootOverride = null;
        MainViewModel.GuardDelay = TimeSpan.FromSeconds(4);
        Notes.Use(null);
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    MainViewModel NewVm(bool guard, bool lockAfterSave = true) =>
        new(_players, new AppSettings { LockAfterSave = lockAfterSave, GuardPinned = guard }, new PinStore());

    static SettingVm Find(MainViewModel vm, string name, int variant = 0) =>
        vm.Sections.SelectMany(s => s.All).Single(v => v.Model.Name == name && v.Model.Variant == variant);

    static string Text(string path) => File.ReadAllText(path);
    string Original() => Encoding.UTF8.GetString(_original);

    [Fact]
    public void Pinning_stores_the_value_follows_edits_and_persists()
    {
        var vm = NewVm(guard: true);
        var fps = Find(vm, "MaxFpsInGame");

        fps.IsPinned = true;
        Assert.Equal("100", vm.Pins.For(_systemFile).Single().Value);
        Assert.Equal(1, vm.PinnedCount);

        fps.NumberValue = 158;
        Assert.Equal("158", vm.Pins.For(_systemFile).Single().Value);
        Assert.False(fps.PinMismatch);

        fps.IsPinned = false;
        Assert.Empty(vm.Pins.For(_systemFile));
        Assert.Equal(0, vm.PinnedCount);

        fps.IsPinned = true; // pinned at the unsaved 158
        var again = new MainViewModel(_players, new AppSettings(), PinStore.Load());
        var fps2 = Find(again, "MaxFpsInGame");
        Assert.True(fps2.IsPinned);
        Assert.Equal("158", fps2.PinnedValue);
        Assert.True(fps2.PinMismatch, "the file still says 100");
        Assert.True(again.ApplyPinsNowCommand.CanExecute(null));
        again.ApplyPinsNowCommand.Execute(null);
        Assert.Equal("158", fps2.Value);
        Assert.Equal(1, again.ChangeCount);
    }

    [Fact]
    public void Guard_service_restores_only_pinned_values_and_keeps_the_lock_state()
    {
        string gameWrote = Original()
            .Replace("MaxFpsInGame@0;13305;58861 = 100", "MaxFpsInGame@0;13305;58861 = 60")
            .Replace("VoicePushToTalk@0;57752;20945 = true", "VoicePushToTalk@0;57752;20945 = false");
        File.WriteAllText(_systemFile, gameWrote);
        var pins = new List<PinnedValue>
        {
            new() { Name = "MaxFpsInGame", Variant = 0, Value = "158" },
            new() { Name = "Gone", Variant = 0, Value = "1" },
        };

        var r = GuardService.Reapply(_systemFile, pins, lockAfter: true);

        Assert.Null(r.Error);
        Assert.Equal(1, r.Restored);
        Assert.Equal(new[] { "Gone" }, r.Missing);
        Assert.NotNull(r.Backup);
        string after = Text(_systemFile);
        Assert.Contains("MaxFpsInGame@0;13305;58861 = 158 //", after);
        Assert.Contains("VoicePushToTalk@0;57752;20945 = false", after); // the game's own change stays
        Assert.True(GuardService.IsLocked(_systemFile));
        Assert.Equal(gameWrote, Text(BackupService.List(_systemFile)[0].Path));

        var r2 = GuardService.Reapply(_systemFile, pins.Take(1).ToList());
        Assert.Equal(0, r2.Restored);
        Assert.Null(r2.Backup);
        Assert.Single(BackupService.List(_systemFile));
        Assert.True(GuardService.IsLocked(_systemFile), "lock left as it was");
    }

    [Fact]
    public void Guard_unlocks_while_the_game_runs_and_restores_pins_after_it_exits()
    {
        bool running = false;
        GameProcess.Override = () => running;
        var vm = NewVm(guard: true);
        vm.RefreshGameState();
        var fps = Find(vm, "MaxFpsInGame");
        fps.NumberValue = 158;
        fps.IsPinned = true;
        vm.SaveCommand.Execute(null);
        Assert.True(vm.IsLocked);
        string? notice = null;
        vm.GuardApplied += m => notice = m;

        running = true;
        vm.Tick();
        Assert.True(vm.IsGameRunning);
        Assert.False(GuardService.IsLocked(_systemFile), "unlocked so the in-game menu can save");

        // The game saves its own idea of the settings and exits.
        File.WriteAllText(_systemFile, Text(_systemFile)
            .Replace("MaxFpsInGame@0;13305;58861 = 158", "MaxFpsInGame@0;13305;58861 = 60")
            .Replace("VoicePushToTalk@0;57752;20945 = true", "VoicePushToTalk@0;57752;20945 = false"));
        running = false;
        vm.Tick();

        string after = Text(_systemFile);
        Assert.Contains("MaxFpsInGame@0;13305;58861 = 158 //", after);  // the pin is back
        Assert.Contains("VoicePushToTalk@0;57752;20945 = false", after); // the game's other change stays
        Assert.True(GuardService.IsLocked(_systemFile), "re-locked because it was locked before the game started");
        Assert.NotNull(notice);
        Assert.Contains("1 value", notice);
        Assert.Equal("158", Find(vm, "MaxFpsInGame").Value);
        Assert.Equal("false", Find(vm, "VoicePushToTalk").Value);
        Assert.False(vm.IsStale);
        Assert.Equal(0, vm.ChangeCount);
    }

    [Fact]
    public void Guard_off_means_nothing_happens_when_the_game_exits()
    {
        bool running = false;
        GameProcess.Override = () => running;
        var vm = NewVm(guard: false);
        vm.RefreshGameState();
        Find(vm, "MaxFpsInGame").IsPinned = true;
        running = true; vm.Tick();
        Assert.False(GuardService.IsLocked(_systemFile));
        File.WriteAllText(_systemFile, Original().Replace("MaxFpsInGame@0;13305;58861 = 100", "MaxFpsInGame@0;13305;58861 = 60"));
        running = false; vm.Tick();
        Assert.Contains("MaxFpsInGame@0;13305;58861 = 60", Text(_systemFile));
        Assert.True(vm.IsStale, "the window notices the file changed on disk");
    }

    [Fact]
    public void Disk_changes_are_noticed_and_edits_survive_a_reload()
    {
        var vm = NewVm(guard: false);
        Find(vm, "MaxFpsInGame").NumberValue = 158;
        File.WriteAllText(_systemFile, Original().Replace("VoicePushToTalk@0;57752;20945 = true", "VoicePushToTalk@0;57752;20945 = false"));

        vm.Tick();
        Assert.True(vm.IsStale);

        int lost = vm.ReloadKeepingEdits();

        Assert.Equal(0, lost);
        Assert.False(vm.IsStale);
        Assert.Equal("158", Find(vm, "MaxFpsInGame").Value);
        Assert.True(Find(vm, "MaxFpsInGame").IsModified);
        Assert.Equal("false", Find(vm, "VoicePushToTalk").Value);
        Assert.Equal(1, vm.ChangeCount);
    }

    [Fact]
    public void Saving_a_stale_file_asks_first_and_keeps_both_sides()
    {
        var vm = NewVm(guard: false, lockAfterSave: false);
        Find(vm, "MaxFpsInGame").NumberValue = 158;
        File.WriteAllText(_systemFile, Original().Replace("VoicePushToTalk@0;57752;20945 = true", "VoicePushToTalk@0;57752;20945 = false"));
        var questions = new List<string>();
        vm.Confirm = (title, _) => { questions.Add(title); return true; };

        vm.SaveCommand.Execute(null);

        Assert.Contains("The file changed on disk", questions);
        string after = Text(_systemFile);
        Assert.Contains("MaxFpsInGame@0;13305;58861 = 158 //", after);
        Assert.Contains("VoicePushToTalk@0;57752;20945 = false", after);
        Assert.Equal(0, vm.ChangeCount);

        // Declining cancels the save and loses nothing.
        var vm2 = NewVm(guard: false);
        Find(vm2, "MaxFpsInGame").NumberValue = 144;
        File.WriteAllBytes(_systemFile, _original);
        vm2.Confirm = (_, _) => false;
        vm2.SaveCommand.Execute(null);
        Assert.Equal(_original, File.ReadAllBytes(_systemFile));
        Assert.Equal(1, vm2.ChangeCount);
    }

    [Fact]
    public void Lock_all_and_unlock_all_touch_only_the_games_own_files()
    {
        string copy = Path.Combine(_players, "s.1.0.cod26.txt_bak.txt");
        File.WriteAllBytes(copy, _original);
        var vm = NewVm(guard: false);

        vm.LockAllCommand.Execute(null);
        Assert.True(GuardService.IsLocked(_systemFile));
        Assert.True(GuardService.IsLocked(_profileFile));
        Assert.False(GuardService.IsLocked(copy));
        Assert.True(vm.IsLocked);
        Assert.All(vm.Files.Where(f => f.Info.IsGameFile), f => Assert.True(f.IsLocked));

        vm.UnlockAllCommand.Execute(null);
        Assert.False(GuardService.IsLocked(_systemFile));
        Assert.False(GuardService.IsLocked(_profileFile));
        Assert.False(vm.IsLocked);
    }

    [Fact]
    public void Backups_are_pruned_to_the_newest_thirty()
    {
        for (int i = 0; i < 35; i++) BackupService.Create(_systemFile);
        Assert.Equal(BackupService.Keep, BackupService.List(_systemFile).Count);
    }

    [Fact]
    public void Notes_mark_hidden_settings_and_a_local_file_overrides_them()
    {
        Notes.Use(null);
        Assert.True(Notes.For("RendererWorkerCount")!.Hidden);
        Assert.Null(Notes.For("NoSuchSetting"));
        Assert.False(Notes.All.ContainsKey("MaxFpsInGame") && Notes.For("MaxFpsInGame")!.Hidden);

        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.NotesOverride, "{ \"MaxFpsInGame\": { \"note\": \"mine\", \"hidden\": true, \"suggested\": \"141\" } }");
        Notes.Use(null);
        Assert.Equal("mine", Notes.For("MaxFpsInGame")!.Note);

        var vm = NewVm(guard: false);
        var fps = Find(vm, "MaxFpsInGame");
        Assert.True(fps.IsHidden);
        Assert.Equal("mine  Suggested: 141.", fps.NoteText);
        Assert.True(fps.Matches("mine"), "notes are searchable");

        vm.OnlyHidden = true;
        var visible = vm.Sections.SelectMany(s => s.Visible).ToList();
        Assert.Contains(fps, visible);
        Assert.All(visible, v => Assert.True(v.IsHidden));
    }

    [Fact]
    public void Command_line_apply_writes_the_matching_files_and_locks_on_request()
    {
        var doc = SettingsDocument.Load(_systemFile);
        var preset = Preset.From("cod26", "system", "System settings (cod26)", new[] { (doc.Match("MaxFpsInGame", 0)!, "158") });
        string presetPath = Path.Combine(_root, "p.json");
        preset.Save(presetPath);
        var log = new StringWriter();

        int code = CliApply.Run(new[] { "--apply", presetPath, "--players", _players, "--dry-run" }, log);
        Assert.Equal(0, code);
        Assert.Contains("1 value(s) to change", log.ToString());
        Assert.Equal(_original, File.ReadAllBytes(_systemFile));

        code = CliApply.Run(new[] { "--apply", presetPath, "--players", _players, "--lock" }, log);
        Assert.Equal(0, code);
        Assert.Contains("MaxFpsInGame@0;13305;58861 = 158 //", Text(_systemFile));
        Assert.True(GuardService.IsLocked(_systemFile));
        Assert.Equal(_original, File.ReadAllBytes(_profileFile)); // a system preset leaves profile files alone
        Assert.Single(BackupService.List(_systemFile));

        Assert.Equal(2, CliApply.Run(new[] { "--apply", presetPath, "--players", Path.Combine(_root, "nowhere") }, log));
        Assert.Equal(1, CliApply.Run(new[] { "--apply", Path.Combine(_root, "missing.json") }, log));
        Assert.Equal(1, CliApply.Run(new[] { "--players", _players }, log));
    }

    [Fact]
    public void Export_preset_holds_every_editable_value_or_only_the_pins()
    {
        var vm = NewVm(guard: false);
        Find(vm, "MaxFpsInGame").IsPinned = true;
        var all = vm.BuildPreset(pinnedOnly: false);
        var pinned = vm.BuildPreset(pinnedOnly: true);
        Assert.Equal("cod26", all.Game);
        Assert.Equal("system", all.Kind);
        Assert.Equal(vm.Sections.SelectMany(s => s.All).Count(v => v.IsEditable), all.Values.Count);
        Assert.Single(pinned.Values);
        Assert.Equal("MaxFpsInGame", pinned.Values[0].Name);
    }
}
