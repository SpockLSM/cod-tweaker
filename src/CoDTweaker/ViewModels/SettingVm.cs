using System.Globalization;
using CoDTweaker.Models;

namespace CoDTweaker.ViewModels;

public sealed class SettingVm : ObservableObject
{
    string _value;

    public Setting Model { get; }
    public RelayCommand ResetCommand { get; }

    public SettingVm(Setting model)
    {
        Model = model;
        _value = model.OriginalValue;
        ResetCommand = new RelayCommand(Reset);
    }

    public string DisplayName => Model.Variant == 0 ? Model.Name : $"{Model.Name}  [variant {Model.Variant}]";

    public string Description => Model.IsProtected
        ? (Model.Description.Length > 0 ? Model.Description + "  (detected by the game, not editable)" : "Detected by the game, not editable")
        : Model.Description;

    public string RangeText => Model.Kind == SettingKind.Number ? $"{Short(Model.Min)} to {Short(Model.Max)}" : "";

    // 0.000000 -> 0, 1.500000 -> 1.5, huge sentinel bounds -> "any"
    static string Short(double v) =>
        Math.Abs(v) >= 1e15 ? "any" : v.ToString("0.######", CultureInfo.InvariantCulture);

    public SettingKind Kind => Model.Kind;
    public bool IsEditable => !Model.IsProtected;
    public bool HasSlider => Model.HasSlider;
    public double Min => Model.Min;
    public double Max => Model.Max;
    public double Step => Model.IsFloat ? Math.Pow(10, -Math.Min(Model.Decimals, 3)) : 1;

    /// <summary>The choices shown in the dropdown. Includes the current value even if the game's list doesn't, so nothing is hidden.</summary>
    public IReadOnlyList<string> DisplayOptions =>
        Model.Options.Contains(_value) ? Model.Options : Model.Options.Concat(new[] { _value }).ToList();

    public string Value
    {
        get => _value;
        set
        {
            var normalized = Model.Normalize(value ?? "");
            if (normalized == null) { OnPropertyChanged(); OnPropertyChanged(nameof(NumberValue)); return; } // invalid: keep what we had
            if (_value == normalized) { OnPropertyChanged(); return; }
            _value = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(NumberValue));
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(DisplayOptions));
        }
    }

    public bool BoolValue
    {
        get => _value == "true";
        set => Value = value ? "true" : "false";
    }

    public double NumberValue
    {
        get => double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Model.Min;
        set => Value = Model.FormatNumber(value);
    }

    public bool IsModified => _value != Model.OriginalValue;

    public void Reset() => Value = Model.OriginalValue;

    public bool Matches(string search) =>
        search.Length == 0 ||
        Model.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        Model.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        _value.Contains(search, StringComparison.OrdinalIgnoreCase);
}

public sealed class SectionVm : ObservableObject
{
    public string Name { get; }
    public List<SettingVm> All { get; }
    public List<SettingVm> Visible { get; private set; }
    public bool IsVisible => Visible.Count > 0;
    public string CountText => Visible.Count == All.Count ? $"{All.Count}" : $"{Visible.Count} of {All.Count}";

    public SectionVm(Section section)
    {
        Name = section.Name;
        All = section.Settings.Select(s => new SettingVm(s)).ToList();
        Visible = All;
    }

    public void Filter(string search, bool onlyModified)
    {
        Visible = All.Where(s => s.Matches(search) && (!onlyModified || s.IsModified)).ToList();
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CountText));
    }
}
