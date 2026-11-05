using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _suppressToggle;

    public ModItemViewModel(ModItem model, MainViewModel owner)
    {
        _owner = owner;
        Model = model;
        _isEnabled = model.IsEnabled;
    }

    public ModItem Model { get; private set; }

    public string Name => Model.Name;
    public string Initial => string.IsNullOrEmpty(Model.Name) ? "?" : Model.Name[..1].ToUpperInvariant();
    public bool HasScripts => Model.HasScripts;
    public bool HasProblems => Model.Problems.Count > 0;
    public string ProblemsText => string.Join(Environment.NewLine, Model.Problems);
    public string DateText => Model.ModifiedAt.ToString("dd.MM.yyyy");

    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (Model.PackageCount > 0) parts.Add(Text.Plural(Model.PackageCount, "файл", "файла", "файлов") + " .package");
            if (Model.ScriptCount > 0) parts.Add(Text.Plural(Model.ScriptCount, "скрипт", "скрипта", "скриптов"));
            parts.Add(Text.Size(Model.SizeBytes));
            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggle) return;
        var updated = _owner.TrySetEnabled(Model, value);
        if (updated is null)
        {
            // Не получилось переместить — возвращаем переключатель обратно.
            _suppressToggle = true;
            IsEnabled = !value;
            _suppressToggle = false;
            return;
        }
        Model = updated;
    }
}
