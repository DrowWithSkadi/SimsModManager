using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Content;
using SimsModManager.Core.Dbpf;
using SimsModManager.Core;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    private bool? _isChecked;

    /// <summary>Значение отличается от исходного — у чипа появляется точка.</summary>
    public bool IsChanged => IsChecked != Original;

    partial void OnIsCheckedChanged(bool? value) => Changed?.Invoke();
    public event Action? Changed;
}

/// <summary>Расцветка в левой колонке редактора.</summary>
public sealed partial class SwatchChoice(int index, ImageSource? image) : ObservableObject
{
    public int Index { get; } = index;
    public ImageSource? Image { get; } = image;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Экран «Настройка одежды»: для одной вещи или сразу для многих.</summary>
public sealed partial class ClothingEditorViewModel : ObservableObject
{
    /// <summary>Перечитать подписи после смены языка.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    private readonly IReadOnlyList<CasItemViewModel> _targets;
    private readonly GenderChoice _originalGender;

    public ClothingEditorViewModel(IReadOnlyList<CasItemViewModel> targets, ImageSource? preview, string? collectionName = null, int collectionTotal = 0)
    {
        _targets = targets;
        IsBulk = targets.Count > 1;
        var all = targets.SelectMany(t => t.Item.Swatches).Select(s => s.Settings).ToList();
        SwatchTotal = all.Count;

        if (IsBulk)
        {
            Title = L.T("Изменить ") + L.P(targets.Count, "вещь|вещи|вещей");
            var mods = targets.Select(t => t.Mod).Distinct().ToList();
            var source = collectionName ?? (mods.Count == 1 ? mods[0].Name : L.T("Из ") + L.P(mods.Count, "мода|модов|модов"));
            Subtitle = collectionTotal > 0 ? L.F("{0} / Выбрано {1} из {2}", source, targets.Count, collectionTotal) : $"{source} / {L.P(all.Count, "расцветка|расцветки|расцветок")}";
            BulkDescription = string.Join(", ", targets.Select(t => t.Item.BodyTypeName.ToLowerInvariant()).Distinct().Take(4)) + " — "
                              + (mods.Count == 1 ? L.T("из одного мода.") : L.T("из ") + L.P(mods.Count, "мода|модов|модов") + ".");
            foreach (var img in targets.Select(t => t.Preview).Where(p => p is not null).Take(2)) BulkPreviews.Add(img!);
        }
        else
        {
            var item = targets[0];
            Title = item.Name;
            Subtitle = item.Name + L.T(" / Изменения в свойствах CAS");
            BodyTypeName = item.Item.BodyTypeName;
            Preview = preview ?? item.Preview;
            for (var i = 0; i < item.Item.Swatches.Count; i++)
            {
                item.ThumbsByInstance.TryGetValue(item.Item.Swatches[i].Entry.Key.Instance, out var img);
                var choice = new SwatchChoice(i, img) { IsSelected = i == 0 };
                if (i < 12) Swatches.Add(choice);
            }
        }

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
        foreach (var (value, name) in OutfitTags.Known.Where(k => k.Value != OutfitTags.Bathing && k.Value != OutfitTags.Situation))
            Outfits.Add(Watch(new TriOption(L.T(name), Common(all, s => s.Outfits.Contains(value))) { TagValue = value }));
        foreach (var (value, name) in OutfitTags.Weather)
            Weather.Add(Watch(new TriOption(L.T(name), Common(all, s => s.Outfits.Contains(value))) { TagValue = value }));
        foreach (var (value, name) in SeasonTags.Known)
            Seasons.Add(Watch(new TriOption(L.T(name), Common(all, s => s.Seasons?.Contains(value) == true)) { TagValue = value }));
        Random = Watch(new TriOption(L.T("Случайные симы"), Common(all, s => s.AllowRandom)));

        HasMixedValues = _originalGender == GenderChoice.Mixed || AllOptions.Any(o => o.Original is null);
    }

    private TriOption Watch(TriOption o)
    {
        o.Changed += NotifyChanges;
        return o;
    }

    private void NotifyChanges()
    {
        foreach (var name in new[] { nameof(HasChanges), nameof(AgesChanged), nameof(OutfitsChanged), nameof(WeatherChanged), nameof(SeasonsChanged), nameof(RandomChanged),
                                     nameof(RandomIsMixed), nameof(RandomOn), nameof(ChangeSummary) })
            OnPropertyChanged(name);
    }

    private static bool? Common(IEnumerable<CasSettings> all, Func<CasSettings, bool> pick)
    {
        var values = all.Select(pick).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }

    // ---------- Что показываем ----------

    public string Title { get; }
    public string PageTitle => IsBulk ? Title : L.T("Настройка одежды");
    public string Subtitle { get; }
    public bool IsBulk { get; }
    public bool IsSingle => !IsBulk;
    public string BodyTypeName { get; } = "";
    public ImageSource? Preview { get; }
    public bool HasPreview => Preview is not null;
    public int SwatchTotal { get; }
    public string BulkCountText => L.P(_targets.Count, "вещь|вещи|вещей");
    public string BulkDescription { get; } = "";
    public ObservableCollection<ImageSource> BulkPreviews { get; } = [];
    public bool HasMixedValues { get; }

    public ObservableCollection<SwatchChoice> Swatches { get; } = [];
    public ObservableCollection<TriOption> Ages { get; } = [];
    public ObservableCollection<TriOption> Outfits { get; } = [];
    /// <summary>Жаркая / холодная погода (метки категории одежды).</summary>
    public ObservableCollection<TriOption> Weather { get; } = [];
    /// <summary>Весна, лето, осень, зима (метки стиля для фильтра в редакторе).</summary>
    public ObservableCollection<TriOption> Seasons { get; } = [];
    public TriOption Random { get; }

    private IEnumerable<TriOption> AllOptions => Ages.Concat(Outfits).Concat(Weather).Concat(Seasons).Append(Random);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwatchText), nameof(ChangeSummary))]
    private int _selectedSwatch;

    public string SwatchText => L.F("Расцветка {0} из {1}", SelectedSwatch + 1, SwatchTotal);

    /// <summary>Менять все расцветки вещи или только выбранную слева.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeSummary))]
    private bool _applyToAllSwatches = true;

    [RelayCommand]
    private void SelectSwatch(SwatchChoice? choice)
    {
        if (choice is null) return;
        foreach (var s in Swatches) s.IsSelected = s == choice;
        SelectedSwatch = choice.Index;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges), nameof(GenderIsMixed), nameof(GenderChanged), nameof(ChangeSummary))]
    private GenderChoice _gender;

    public bool GenderIsMixed => Gender == GenderChoice.Mixed;
    public bool GenderChanged => Gender != _originalGender;
    public bool AgesChanged => Ages.Any(a => a.IsChanged);
    public bool OutfitsChanged => Outfits.Any(o => o.IsChanged);
    public bool WeatherChanged => Weather.Any(o => o.IsChanged);
    public bool SeasonsChanged => Seasons.Any(o => o.IsChanged);
    public bool RandomChanged => Random.IsChanged;
    public bool RandomIsMixed => Random.IsChecked is null;

    public bool HasChanges => GenderChanged || AgesChanged || OutfitsChanged || WeatherChanged || SeasonsChanged || RandomChanged;

    /// <summary>Для строки состояния: «Изменено 3 параметра · 12 расцветок».</summary>
    public string ChangeSummary
    {
        get
        {
            var count = (GenderChanged ? 1 : 0) + AllOptions.Count(o => o.IsChanged);
            if (count == 0) return L.T("Изменений пока нет");
            var scope = IsBulk
                ? L.P(_targets.Count, "вещь|вещи|вещей")
                : ApplyToAllSwatches ? L.P(SwatchTotal, "расцветка|расцветки|расцветок") : L.T("1 расцветка");
            return L.F("Изменено {0} · {1}", L.P(count, "параметр|параметра|параметров"), scope);
        }
    }

    /// <summary>Переключатель «Случайные симы»; при «по-разному» показан средним положением.</summary>
    public bool? RandomOn
    {
        get => Random.IsChecked;
        set
        {
            Random.IsChecked = value ?? true;
            OnPropertyChanged();
        }
    }

    /// <summary>Есть ли у кого-то из настраиваемых модов сохранённый оригинал.</summary>
    public bool CanRestoreAuthor => _targets.Any(t => t.Mod.IsModified);
    public IReadOnlyList<ModItemViewModel> TargetMods => _targets.Select(t => t.Mod).Distinct().ToList();

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
            case "formal":
                foreach (var o in Outfits) o.IsChecked = o.TagValue == OutfitTags.Formal;
                break;
        }
        NotifyChanges();
    }

    [RelayCommand]
    private void Reset()
    {
        Gender = _originalGender;
        foreach (var o in AllOptions) o.IsChecked = o.Original;
        NotifyChanges();
    }

    // ---------- Подсчёт изменений ----------

    /// <summary>Новые настройки для каждой расцветки, сгруппированные по вещам (файлам).</summary>
    public IReadOnlyList<(CasItemViewModel Target, Dictionary<ResourceKey, CasSettings> Changes)> BuildChanges()
    {
        var result = new List<(CasItemViewModel, Dictionary<ResourceKey, CasSettings>)>();
        foreach (var target in _targets)
        {
            var changes = new Dictionary<ResourceKey, CasSettings>();
            var swatches = !IsBulk && !ApplyToAllSwatches
                ? target.Item.Swatches.Skip(SelectedSwatch).Take(1)
                : target.Item.Swatches;

            foreach (var swatch in swatches)
            {
                var cur = swatch.Settings;

                var ages = cur.Ages;
                foreach (var a in Ages.Where(a => a.IsChecked is not null && a.IsChanged))
                    ages = a.IsChecked == true ? ages | a.Age : ages & ~a.Age;

                var (female, male) = Gender switch
                {
                    _ when !GenderChanged => (cur.Female, cur.Male),
                    GenderChoice.Female => (true, false),
                    GenderChoice.Male => (false, true),
                    GenderChoice.Both => (true, true),
                    _ => (cur.Female, cur.Male),
                };

                var outfits = cur.Outfits.ToHashSet();
                Toggle(outfits, Outfits.Concat(Weather));

                var seasons = cur.Seasons?.ToHashSet() ?? new HashSet<uint>();
                Toggle(seasons, Seasons);

                var random = RandomChanged && Random.IsChecked is { } r ? r : cur.AllowRandom;
                var updated = new CasSettings(ages, female, male, outfits, random, seasons);
                if (!updated.SameAs(cur)) changes[swatch.Entry.Key] = updated;
            }
            if (changes.Count > 0) result.Add((target, changes));
        }
        return result;
    }

    /// <summary>Включает или убирает метки по изменённым чипам; «по-разному» оставляет как было у каждой вещи.</summary>
    private static void Toggle(HashSet<uint> set, IEnumerable<TriOption> options)
    {
        foreach (var o in options.Where(o => o.IsChecked is not null && o.IsChanged))
        {
            if (o.IsChecked == true) set.Add(o.TagValue);
            else set.Remove(o.TagValue);
        }
    }

    /// <summary>Предупреждение, если после изменений вещь не будет доступна никому.</summary>
    public string? Validate()
    {
        if (Ages.All(a => a.IsChecked == false)) return L.T("Выберите хотя бы один возраст — иначе вещь пропадёт из игры.");
        if (Outfits.Concat(Weather).All(o => o.IsChecked == false))
            return L.T("Не выбрана ни одна категория одежды. Вещь будет видна в редакторе, но игра не будет её использовать в нарядах.");
        return null;
    }
}
