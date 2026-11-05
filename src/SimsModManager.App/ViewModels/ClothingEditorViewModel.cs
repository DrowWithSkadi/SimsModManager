using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Content;
using SimsModManager.Core.Dbpf;

namespace SimsModManager.App.ViewModels;

public enum GenderChoice { Mixed, Female, Male, Both }

/// <summary>
/// Флажок, который может быть «да», «нет» или «по-разному» (null) — когда настраиваем
/// сразу несколько вещей с разными значениями.
/// </summary>
public sealed partial class TriOption : ObservableObject
{
    public TriOption(string name, bool? value)
    {
        Name = name;
        _isChecked = value;
        Original = value;
    }

    public string Name { get; }
    public bool? Original { get; }
    public uint TagValue { get; init; }
    public AgeGender Age { get; init; }

    [ObservableProperty] private bool? _isChecked;

    public bool IsChanged => IsChecked != Original;
    partial void OnIsCheckedChanged(bool? value) => Changed?.Invoke();
    public event Action? Changed;
}

/// <summary>Экран «Настройка одежды»: для одной вещи или сразу для многих.</summary>
public sealed partial class ClothingEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<CasItemViewModel> _targets;
    private readonly GenderChoice _originalGender;

    public ClothingEditorViewModel(IReadOnlyList<CasItemViewModel> targets, ImageSource? preview)
    {
        _targets = targets;
        Preview = preview;
        var all = targets.SelectMany(t => t.Item.Swatches).Select(s => s.Settings).ToList();

        Title = targets.Count == 1 ? targets[0].Name : $"Изменить {Text.Plural(targets.Count, "вещь", "вещи", "вещей")}";
        Subtitle = targets.Count == 1
            ? $"{targets[0].Item.BodyTypeName} · {Text.Plural(targets[0].SwatchCount, "расцветка", "расцветки", "расцветок")}"
            : $"Из {Text.Plural(targets.Select(t => t.Mod).Distinct().Count(), "мода", "модов", "модов")} · "
              + Text.Plural(all.Count, "расцветка", "расцветки", "расцветок");
        IsBulk = targets.Count > 1;

        var female = Common(all, s => s.Female);
        var male = Common(all, s => s.Male);
        _originalGender = (female, male) switch
        {
            (true, false) => GenderChoice.Female,
            (false, true) => GenderChoice.Male,
            (true, true) => GenderChoice.Both,
            _ => GenderChoice.Mixed,
        };
        _gender = _originalGender;

        foreach (var (age, name) in Describe.AgeNames)
            Ages.Add(Watch(new TriOption(name, Common(all, s => s.Ages.HasFlag(age))) { Age = age }));
        foreach (var (value, name) in OutfitTags.Known)
            Outfits.Add(Watch(new TriOption(name, Common(all, s => s.Outfits.Contains(value))) { TagValue = value }));
        Random = Watch(new TriOption("Случайные симы", Common(all, s => s.AllowRandom)));
        Random.Changed += () => OnPropertyChanged(nameof(RandomIsMixed));
    }

    private TriOption Watch(TriOption o)
    {
        o.Changed += () => OnPropertyChanged(nameof(HasChanges));
        return o;
    }

    private static bool? Common(IEnumerable<CasSettings> all, Func<CasSettings, bool> pick)
    {
        var values = all.Select(pick).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public bool IsBulk { get; }
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public bool RandomIsMixed => Random.IsChecked is null;

    public ObservableCollection<TriOption> Ages { get; } = [];
    public ObservableCollection<TriOption> Outfits { get; } = [];
    public TriOption Random { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(GenderIsMixed))]
    private GenderChoice _gender;

    public bool GenderIsMixed => Gender == GenderChoice.Mixed;

    public bool HasChanges =>
        Gender != _originalGender || Ages.Any(a => a.IsChanged) || Outfits.Any(o => o.IsChanged) || Random.IsChanged;

    /// <summary>Нажатие на «Случайные симы» в строке — переключает флажок.</summary>
    public bool RandomOn
    {
        get => Random.IsChecked == true;
        set
        {
            Random.IsChecked = value;
            OnPropertyChanged();
        }
    }

    // ---------- Быстрые наборы ----------

    [RelayCommand]
    private void Preset(string name)
    {
        switch (name)
        {
            case "women":
                Gender = GenderChoice.Female;
                RandomOn = false;
                break;
            case "men":
                Gender = GenderChoice.Male;
                RandomOn = false;
                break;
            case "formal":
                foreach (var o in Outfits) o.IsChecked = o.TagValue == OutfitTags.Formal;
                break;
            case "everyday":
                foreach (var o in Outfits) o.IsChecked = o.TagValue is OutfitTags.Everyday;
                break;
            case "adults":
                foreach (var a in Ages) a.IsChecked = a.Age is AgeGender.Teen or AgeGender.YoungAdult or AgeGender.Adult or AgeGender.Elder;
                break;
        }
        OnPropertyChanged(nameof(HasChanges));
    }

    [RelayCommand]
    private void Reset()
    {
        Gender = _originalGender;
        foreach (var o in Ages.Concat(Outfits)) o.IsChecked = o.Original;
        Random.IsChecked = Random.Original;
        OnPropertyChanged(nameof(RandomOn));
    }

    // ---------- Подсчёт изменений ----------

    /// <summary>Новые настройки для каждой расцветки, сгруппированные по файлам.</summary>
    public IReadOnlyList<(CasItemViewModel Target, Dictionary<ResourceKey, CasSettings> Changes)> BuildChanges()
    {
        var result = new List<(CasItemViewModel, Dictionary<ResourceKey, CasSettings>)>();
        foreach (var target in _targets)
        {
            var changes = new Dictionary<ResourceKey, CasSettings>();
            foreach (var swatch in target.Item.Swatches)
            {
                var cur = swatch.Settings;

                var ages = cur.Ages;
                foreach (var a in Ages.Where(a => a.IsChecked is not null))
                    ages = a.IsChecked == true ? ages | a.Age : ages & ~a.Age;

                var (female, male) = Gender switch
                {
                    GenderChoice.Female => (true, false),
                    GenderChoice.Male => (false, true),
                    GenderChoice.Both => (true, true),
                    _ => (cur.Female, cur.Male),
                };

                var outfits = cur.Outfits.ToHashSet();
                foreach (var o in Outfits.Where(o => o.IsChecked is not null))
                {
                    if (o.IsChecked == true) outfits.Add(o.TagValue);
                    else outfits.Remove(o.TagValue);
                }

                var random = Random.IsChecked ?? cur.AllowRandom;
                var updated = new CasSettings(ages, female, male, outfits, random);
                if (!updated.SameAs(cur)) changes[swatch.Entry.Key] = updated;
            }
            if (changes.Count > 0) result.Add((target, changes));
        }
        return result;
    }

    /// <summary>Предупреждение, если после изменений вещь не будет доступна никому.</summary>
    public string? Validate()
    {
        if (Ages.All(a => a.IsChecked == false)) return "Выберите хотя бы один возраст — иначе вещь пропадёт из игры.";
        if (Outfits.All(o => o.IsChecked == false))
            return "Не выбрана ни одна категория одежды. Вещь будет видна в редакторе, но игра не будет её использовать в нарядах.";
        return null;
    }
}
