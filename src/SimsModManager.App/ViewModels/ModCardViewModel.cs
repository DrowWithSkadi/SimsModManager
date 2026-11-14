using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.App.Services;
using SimsModManager.Core;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

/// <summary>Карточка мода: картинка, сведения, вещи внутри, файлы. Для больших наборов — вид «Сборник».</summary>
public sealed partial class ModCardViewModel : ObservableObject
{
    /// <summary>Перечитать подписи после смены языка.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>С какого числа вещей мод показывается как сборник (сетка с поиском).</summary>
    private const int CollectionThreshold = 4;

    public ModCardViewModel(ModItemViewModel mod, SimsPaths paths, bool isModified)
    {
        Mod = mod;
        _isModified = isModified;
        LocationText = mod.Model.IsTray ? "Tray" : Path.GetRelativePath(paths.Root, mod.Model.Path);

        foreach (var f in mod.Model.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            long size = 0;
            try { size = new FileInfo(f).Length; } catch (IOException) { }
            Files.Add(new FileRow(Path.GetFileName(f), Text.Size(size)));
        }

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => string.IsNullOrWhiteSpace(ItemSearch)
                                || o is CasItemViewModel i && i.Name.Contains(ItemSearch.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    public ModItemViewModel Mod { get; }

    public string Breadcrumb => L.T("Библиотека / ") + (string.IsNullOrEmpty(Mod.CategoryLabel) ? L.T("Мод") : Mod.CategoryLabel)
                                + (IsCollection ? L.T(" / Сборник") : "");
    public string LocationText { get; }
    public string InstalledText => Mod.InstalledAt.ToString("dd.MM.yyyy");
    public string SizeText => Text.Size(Mod.SizeBytes);

    public string FilesSummary
    {
        get
        {
            var m = Mod.Model;
            if (m.IsTray) return L.P(m.Files.Count, "файл|файла|файлов") + L.T(" в Tray");
            var parts = new List<string>();
            if (m.PackageCount > 0) parts.Add($"{m.PackageCount} .package");
            if (m.ScriptCount > 0) parts.Add($"{m.ScriptCount} .ts4script");
            return string.Join(", ", parts);
        }
    }

    public string FilesHeader => L.P(Files.Count, "файл|файла|файлов");
    public string ContentText => L.P(Items.Count, "вещь|вещи|вещей") + " · " + FilesHeader;

    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<CasItemViewModel> Items { get; } = [];
    public ICollectionView ItemsView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    [ObservableProperty] private bool _isLoadingItems = true;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _filesExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    private string? _author;

    public bool HasAuthor => !string.IsNullOrEmpty(Author);

    [ObservableProperty] private string _itemSearch = "";
    [ObservableProperty] private string _itemsCountText = "";

    partial void OnItemSearchChanged(string value)
    {
        ItemsView.Refresh();
        UpdateCounts();
    }

    public bool HasImage => Image is not null;
    public bool HasItems => Items.Count > 0;
    public bool HasManyItems => Items.Count > 1;
    public bool IsCollection => Items.Count >= CollectionThreshold;
    public bool IsSingle => !IsCollection;
    public bool ShowNoItems => !IsLoadingItems && Items.Count == 0;
    public CasItemViewModel? FirstItem => Items.FirstOrDefault();

    public string EditAllText => L.T("Настроить ") + L.P(Items.Count, "вещь|вещи|вещей");

    public string ItemsSummary => Items.Count == 0
        ? ""
        : L.P(Items.Count, "вещь|вещи|вещей") + " · " +
          L.P(Items.Sum(i => i.SwatchCount), "расцветка|расцветки|расцветок");

    public string NoItemsText => Mod.Model.IsTray
        ? L.T("Это участок, комната или семья из галереи. Они хранятся в папке Tray и появляются в игре в «Библиотеке».")
        : Mod.HasScripts && Mod.Model.PackageCount == 0
            ? L.T("Скриптовый мод: в нём нет одежды или предметов, которые можно настроить. Включается и выключается целиком.")
            : L.T("В этом моде нет одежды, причёсок или макияжа, поэтому настраивать пол и категории не нужно.");

    private void UpdateCounts()
    {
        var shown = ItemsView.Cast<object>().Count();
        ItemsCountText = shown == Items.Count
            ? L.P(Items.Count, "вещь|вещи|вещей")
            : L.F("{0} · показано {1}", L.P(Items.Count, "вещь|вещи|вещей"), shown);
    }

    /// <summary>Читает вещи из файлов в фоне — у больших сборников это занимает время.</summary>
    public async Task LoadAsync(ContentCache cache)
    {
        var files = Mod.Model.Files;
        var modName = Mod.Name;
        var isTray = Mod.Model.IsTray;
        var (items, image, author) = await Task.Run(() =>
        {
            var stamp = ContentCache.StampOf(files);
            var thumb = cache.GetThumbnail(stamp);
            var big = thumb is null ? null : ThumbnailFactory.Create(thumb, 600);
            var cas = isTray ? Array.Empty<CasItem>() : CasPackageEditor.ReadItems(files);
            var who = CasPackageEditor.GuessAuthor(modName, cas.SelectMany(i => i.Swatches).Select(s => s.Part.Name).Take(20));
            return (cas, big, who);
        });

        Image = image ?? Mod.Thumbnail;
        Author = author;
        Items.Clear();
        foreach (var item in items) Items.Add(new CasItemViewModel(item, Mod, items.Count == 1));
        IsLoadingItems = false;
        UpdateCounts();
        foreach (var name in new[] { nameof(HasItems), nameof(HasManyItems), nameof(IsCollection), nameof(IsSingle), nameof(ShowNoItems),
                                     nameof(FirstItem), nameof(EditAllText), nameof(ItemsSummary), nameof(ContentText), nameof(Breadcrumb) })
            OnPropertyChanged(name);

        // Картинки расцветок — по одной вещи за раз, чтобы не нагружать диск
        foreach (var item in Items.ToList()) await item.LoadSwatchesAsync();
    }

    public sealed record FileRow(string Name, string Size);
}

/// <summary>Вещь внутри мода (все её расцветки).</summary>
public sealed partial class CasItemViewModel : ObservableObject
{
    public CasItemViewModel(CasItem item, ModItemViewModel mod, bool onlyItem)
    {
        Item = item;
        Mod = mod;
        Name = onlyItem ? mod.Name : PrettyName(item);
    }

    /// <summary>Имя вещи из внутреннего названия: «author_yfTop_UnderwearSexy_Black_123» → «Underwear Sexy».</summary>
    private static string PrettyName(CasItem item)
    {
        var raw = item.Swatches[0].Part.Name;
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        var codeIndex = parts.FindIndex(p => System.Text.RegularExpressions.Regex.IsMatch(p, "^[ytacpe][fmu][A-Z]"));
        var words = (codeIndex >= 0 ? parts.Skip(codeIndex + 1) : parts.Skip(1))
            .Where(p => !p.All(char.IsDigit))
            .Take(2)
            .Select(p => System.Text.RegularExpressions.Regex.Replace(p, "(?<=[a-z])(?=[A-Z])", " "))
            .ToList();
        var name = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(name) ? item.BodyTypeName : name;
    }

    public CasItem Item { get; }
    public ModItemViewModel Mod { get; }
    public string Name { get; }
    public int SwatchCount => Item.Swatches.Count;
    public string SwatchCountText => L.P(SwatchCount, "цвет|цвета|цветов");

    public string Summary
    {
        get
        {
            var s = Item.Swatches[0].Settings;
            return $"{Item.BodyTypeName} · {Describe.Gender(s.Female, s.Male)} · {Describe.Ages(s.Ages)}";
        }
    }

    public string OutfitsText
    {
        get
        {
            var s = Item.Swatches[0].Settings;
            var names = OutfitTags.Known.Concat(OutfitTags.Weather).Where(k => s.Outfits.Contains(k.Value)).Select(k => L.T(k.Name).ToLowerInvariant()).ToList();
            var outfits = names.Count == 0 ? L.T("категории не указаны") : string.Join(", ", names);
            return outfits + (s.AllowRandom ? L.T(" · для случайных симов") : L.T(" · не для случайных симов"));
        }
    }

    /// <summary>Картинки расцветок по порядку (null — у расцветки нет миниатюры).</summary>
    public ObservableCollection<ImageSource> SwatchImages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private ImageSource? _preview;

    public bool HasPreview => Preview is not null;

    public string MoreSwatchesText => SwatchCount > 8 ? "+" + L.P(SwatchCount - 8, "расцветка|расцветки|расцветок") : "";

    /// <summary>Все загруженные миниатюры по номеру расцветки — нужны редактору.</summary>
    public Dictionary<ulong, ImageSource> ThumbsByInstance { get; } = [];

    private bool _loaded;

    public async Task LoadSwatchesAsync()
    {
        if (_loaded) return;
        _loaded = true;
        var path = Item.PackagePath;
        var instances = Item.Swatches.Take(12).Select(s => s.Entry.Key.Instance).ToList();
        var images = await Task.Run(() =>
        {
            var raw = CasPackageEditor.ReadSwatchThumbnails(path, instances);
            return instances
                .Select(i => (i, raw.TryGetValue(i, out var t) ? ThumbnailFactory.Create(t, 160) : null))
                .ToList();
        });

        foreach (var (instance, img) in images)
        {
            if (img is null) continue;
            ThumbsByInstance[instance] = img;
            if (SwatchImages.Count < 8) SwatchImages.Add(img);
        }
        Preview = images.Select(x => x.Item2).FirstOrDefault(x => x is not null) ?? Mod.Thumbnail;
    }
}

/// <summary>Подписи для пола и возраста.</summary>
public static class Describe
{
    public static IReadOnlyList<(AgeGender Age, string Name)> AgeNames =>
    [
        (AgeGender.Infant, L.T("Младенцы")),
        (AgeGender.Toddler, L.T("Тоддлеры")),
        (AgeGender.Child, L.T("Дети")),
        (AgeGender.Teen, L.T("Подростки")),
        (AgeGender.YoungAdult, L.T("Молодые")),
        (AgeGender.Adult, L.T("Взрослые")),
        (AgeGender.Elder, L.T("Пожилые")),
    ];

    public static string Gender(bool female, bool male) => (female, male) switch
    {
        (true, false) => L.T("Женщины"),
        (false, true) => L.T("Мужчины"),
        (true, true) => L.T("Все"),
        _ => L.T("Пол не указан"),
    };

    public static string Ages(AgeGender ages)
    {
        var set = AgeNames.Where(a => ages.HasFlag(a.Age)).ToList();
        if (set.Count == 0) return L.T("возраст не указан");
        if (set.Count == 1) return set[0].Name;
        var all = AgeNames.ToList();
        var idx = set.Select(a => all.IndexOf(a)).ToList();
        var contiguous = idx.Last() - idx.First() == idx.Count - 1;
        return contiguous ? $"{set[0].Name} — {set[^1].Name}" : string.Join(", ", set.Select(a => a.Name));
    }
}
