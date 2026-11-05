using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SimsModManager.App.Services;
using SimsModManager.Core;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

/// <summary>Карточка мода: картинка, сведения, вещи внутри, файлы.</summary>
public sealed partial class ModCardViewModel : ObservableObject
{
    public ModCardViewModel(ModItemViewModel mod, SimsPaths paths, bool isModified)
    {
        Mod = mod;
        _isModified = isModified;

        // Для участка показываем папку Tray, для мода — его папку или файл в Mods
        LocationText = mod.Model.IsTray ? "Tray" : Path.GetRelativePath(paths.Root, mod.Model.Path);

        foreach (var f in mod.Model.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            long size = 0;
            try { size = new FileInfo(f).Length; } catch (IOException) { }
            Files.Add(new FileRow(Path.GetFileName(f), Text.Size(size)));
        }
    }

    public ModItemViewModel Mod { get; }

    public string Breadcrumb => "Библиотека / " + (string.IsNullOrEmpty(Mod.CategoryLabel) ? "Мод" : Mod.CategoryLabel);
    public string LocationText { get; }
    public string InstalledText => Mod.InstalledAt.ToString("dd.MM.yyyy");
    public string SizeText => Text.Size(Mod.SizeBytes);

    public string FilesSummary
    {
        get
        {
            var m = Mod.Model;
            if (m.IsTray) return Text.Plural(m.Files.Count, "файл", "файла", "файлов") + " в Tray";
            var parts = new List<string>();
            if (m.PackageCount > 0) parts.Add($"{m.PackageCount} .package");
            if (m.ScriptCount > 0) parts.Add($"{m.ScriptCount} .ts4script");
            return string.Join(", ", parts);
        }
    }

    public string FilesHeader => Text.Plural(Files.Count, "файл", "файла", "файлов");

    public ObservableCollection<FileRow> Files { get; } = [];
    public ObservableCollection<CasItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    public bool HasImage => Image is not null;
    public bool HasManyItems => Items.Count > 1;
    public bool ShowNoItems => !IsLoadingItems && Items.Count == 0;
    [ObservableProperty] private bool _isLoadingItems = true;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _filesExpanded;

    public bool HasItems => Items.Count > 0;
    public bool IsCas => Mod.Category is ModCategory.Clothing or ModCategory.Hair or ModCategory.Makeup;

    public string ItemsSummary => Items.Count == 0
        ? ""
        : Text.Plural(Items.Count, "вещь", "вещи", "вещей") + " · " +
          Text.Plural(Items.Sum(i => i.SwatchCount), "расцветка", "расцветки", "расцветок");

    public string NoItemsText => Mod.Model.IsTray
        ? "Это участок, комната или семья из галереи. Они хранятся в папке Tray и появляются в игре в «Библиотеке»."
        : Mod.HasScripts && Mod.Model.PackageCount == 0
            ? "Скриптовый мод: в нём нет одежды или предметов, которые можно настроить. Включается и выключается целиком."
            : "В этом моде нет одежды, причёсок или макияжа, поэтому настраивать пол и категории не нужно.";

    /// <summary>Читает вещи из файлов в фоне — у больших сборников это занимает время.</summary>
    public async Task LoadAsync(ContentCache cache)
    {
        var files = Mod.Model.Files;
        var (items, image) = await Task.Run(() =>
        {
            var stamp = ContentCache.StampOf(files);
            var thumb = cache.GetThumbnail(stamp);
            var big = thumb is null ? null : ThumbnailFactory.Create(thumb, 600);
            var cas = Mod.Model.IsTray ? Array.Empty<CasItem>() : CasPackageEditor.ReadItems(files);
            return (cas, big);
        });

        Image = image ?? Mod.Thumbnail;
        Items.Clear();
        foreach (var item in items) Items.Add(new CasItemViewModel(item, Mod, items.Count == 1));
        IsLoadingItems = false;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasManyItems));
        OnPropertyChanged(nameof(ShowNoItems));
        OnPropertyChanged(nameof(ItemsSummary));
        OnPropertyChanged(nameof(IsCas));

        foreach (var item in Items) _ = item.LoadSwatchesAsync();
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
        Name = onlyItem ? mod.Name : $"{item.BodyTypeName} · {Path.GetFileNameWithoutExtension(item.PackagePath)}";
    }

    public CasItem Item { get; }
    public ModItemViewModel Mod { get; }
    public string Name { get; }
    public int SwatchCount => Item.Swatches.Count;

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
            var names = OutfitTags.Known.Where(k => s.Outfits.Contains(k.Value)).Select(k => k.Name.ToLowerInvariant()).ToList();
            var outfits = names.Count == 0 ? "категории не указаны" : string.Join(", ", names);
            return outfits + (s.AllowRandom ? " · для случайных симов" : " · не для случайных симов");
        }
    }

    public ObservableCollection<ImageSource> SwatchImages { get; } = [];
    public string MoreSwatchesText => SwatchCount > 8 ? "+" + Text.Plural(SwatchCount - 8, "расцветка", "расцветки", "расцветок") : "";

    public async Task LoadSwatchesAsync()
    {
        var swatches = Item.Swatches.Take(8).ToList();
        var images = await Task.Run(() => swatches
            .Select(CasPackageEditor.ReadSwatchThumbnail)
            .Select(t => t is null ? null : ThumbnailFactory.Create(t, 64))
            .ToList());
        foreach (var img in images)
            if (img is not null) SwatchImages.Add(img);
    }
}

/// <summary>Подписи для пола и возраста.</summary>
public static class Describe
{
    public static readonly IReadOnlyList<(AgeGender Age, string Name)> AgeNames =
    [
        (AgeGender.Infant, "Младенцы"),
        (AgeGender.Toddler, "Тоддлеры"),
        (AgeGender.Child, "Дети"),
        (AgeGender.Teen, "Подростки"),
        (AgeGender.YoungAdult, "Молодые"),
        (AgeGender.Adult, "Взрослые"),
        (AgeGender.Elder, "Пожилые"),
    ];

    public static string Gender(bool female, bool male) => (female, male) switch
    {
        (true, false) => "Женщины",
        (false, true) => "Мужчины",
        (true, true) => "Все",
        _ => "Пол не указан",
    };

    public static string Ages(AgeGender ages)
    {
        var set = AgeNames.Where(a => ages.HasFlag(a.Age)).ToList();
        if (set.Count == 0) return "возраст не указан";
        if (set.Count == 1) return set[0].Name;
        // Подряд идущие возрасты — диапазоном, иначе списком
        var idx = set.Select(a => AgeNames.ToList().IndexOf(a)).ToList();
        var contiguous = idx.Last() - idx.First() == idx.Count - 1;
        return contiguous ? $"{set[0].Name} — {set[^1].Name}" : string.Join(", ", set.Select(a => a.Name));
    }
}
