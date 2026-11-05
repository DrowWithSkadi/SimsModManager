using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.App.Services;
using SimsModManager.Core;
using SimsModManager.Core.Archives;
using SimsModManager.Core.Content;

namespace SimsModManager.App.ViewModels;

public enum StateFilter { All, Enabled, Disabled }
public enum TypeFilter { All, Clothing, Hair, Makeup, Objects, Scripts, Other, Tray }
public enum AppPage { Library, InstallResult, Settings, NotFound }

/// <summary>
/// Главная модель окна. Разбита на несколько файлов:
/// MainViewModel.cs — библиотека и фильтры, .Install.cs — установка,
/// .Actions.cs — удаление, включение, диалоги и уведомления, .Settings.cs — настройки.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IArchiveExtractor _extractor = new SharpCompressExtractor();
    private readonly ContentCache _cache = new();
    private readonly DispatcherTimer _gameWatch;
    private ModLibrary? _library;
    private ModInstaller? _installer;
    private CancellationTokenSource? _analysisCts;

    public MainViewModel()
    {
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMod;
        _sortBy = _settings.SortBy;
        _sortDescending = _settings.SortDescending;
        _isGridView = _settings.GridView;
        ApplySort();

        _gameWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _gameWatch.Tick += (_, _) => IsGameRunning = GameOptions.IsGameRunning();
        _gameWatch.Start();

        InitSettingsPage();
        InitGameFolder();
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ICollectionView ModsView { get; }

    // ---------- Навигация ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryPage), nameof(IsInstallResultPage), nameof(IsSettingsPage), nameof(IsNotFoundPage), nameof(ShowLibraryChrome))]
    private AppPage _page = AppPage.Library;

    public bool IsLibraryPage => Page == AppPage.Library;
    public bool IsInstallResultPage => Page == AppPage.InstallResult;
    public bool IsSettingsPage => Page == AppPage.Settings;
    public bool IsNotFoundPage => Page == AppPage.NotFound;

    /// <summary>Поиск, фильтры и зона установки видны в библиотеке и на итоге установки.</summary>
    public bool ShowLibraryChrome => Page is AppPage.Library or AppPage.InstallResult;

    [RelayCommand]
    private void GoLibrary() => Page = _library is null ? AppPage.NotFound : AppPage.Library;

    [RelayCommand]
    private void OpenSettings()
    {
        RefreshSettingsPage();
        Page = AppPage.Settings;
    }

    /// <summary>Узкое окно — фильтры типов сворачиваются в выпадающий список.</summary>
    [ObservableProperty] private bool _isCompact;

    // ---------- Папка игры ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    private string? _simsFolder;

    public bool HasGame => _library is not null;

    private void InitGameFolder()
    {
        var folder = _settings.SimsFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            folder = GameLocator.FindSimsFolder();
            if (folder is not null)
            {
                _settings.SimsFolder = folder;
                SaveSettings();
            }
        }
        UseFolder(folder);
    }

    private void UseFolder(string? folder)
    {
        _analysisCts?.Cancel();
        if (folder is null)
        {
            _library = null;
            _installer = null;
            SimsFolder = null;
            Mods.Clear();
            Candidates = GameLocator.GetCandidates();
            Page = AppPage.NotFound;
            UpdateSummary();
            return;
        }

        var paths = new SimsPaths(folder);
        _library = new ModLibrary(paths, CreateDeleter());
        _installer = new ModInstaller(paths, _extractor);
        SimsFolder = paths.Root;
        Page = AppPage.Library;

        try { _library.CleanupTrash(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _ = RefreshAsync();
    }

    private IFileDeleter CreateDeleter() =>
        _settings.DeleteToRecycleBin ? new RecycleBinDeleter() : new PermanentDeleter();

    [ObservableProperty] private IReadOnlyList<GameLocator.Candidate> _candidates = [];

    [RelayCommand]
    private void RetrySearch()
    {
        var found = GameLocator.FindSimsFolder();
        if (found is not null)
        {
            _settings.SimsFolder = found;
            SaveSettings();
            UseFolder(found);
            ShowToast("Папка игры найдена", Icons.Check);
        }
        else
        {
            Candidates = GameLocator.GetCandidates();
            ShowToast("Папка игры по-прежнему не найдена", Icons.Info);
        }
    }

    [RelayCommand]
    private async Task UseCandidate(GameLocator.Candidate? candidate)
    {
        if (candidate is null) return;
        if (!candidate.Exists)
        {
            await ShowDialogAsync(new DialogViewModel
            {
                Title = "Такой папки ещё нет",
                Icon = Icons.FolderSearch,
                Message = "Запустите The Sims 4 один раз и закройте игру — она создаст папку сама. Или укажите другую папку.",
                PathText = candidate.Path,
                PrimaryText = "Понятно",
            });
            return;
        }
        await ApplyFolderAsync(candidate.Path);
    }

    [RelayCommand]
    private async Task ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку The Sims 4 (внутри «Документы\\Electronic Arts»)",
            InitialDirectory = SimsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;

        var folder = dialog.FolderName;
        if (Path.GetFileName(folder).Equals("Mods", StringComparison.OrdinalIgnoreCase))
            folder = Path.GetDirectoryName(folder)!;
        await ApplyFolderAsync(folder);
    }

    private async Task ApplyFolderAsync(string folder)
    {
        if (!GameLocator.LooksLikeSimsFolder(folder))
        {
            var answer = await ShowDialogAsync(new DialogViewModel
            {
                Title = "Это папка The Sims 4?",
                Icon = Icons.FolderSearch,
                Message = "В ней нет Mods, saves или Options.ini. Всё равно использовать?",
                PathText = folder,
                PrimaryText = "Да",
                SecondaryText = "Нет",
            });
            if (answer != DialogResult.Primary) return;
        }

        _settings.SimsFolder = folder;
        SaveSettings();
        UseFolder(folder);
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (_library is null) return;
        Directory.CreateDirectory(_library.Paths.Mods);
        OpenInExplorer(_library.Paths.Mods);
    }

    [RelayCommand]
    private void OpenModLocation(ModItemViewModel? mod)
    {
        if (mod is null) return;
        try
        {
            if (mod.Model.IsTray && mod.Model.Files.Count > 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{mod.Model.Files[0]}\"") { UseShellExecute = true });
            else if (mod.Model.IsFolder) OpenInExplorer(mod.Model.Path);
            else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{mod.Model.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    // ---------- Список модов ----------

    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    private async Task RefreshAsync(ICollection<string>? newModNames = null)
    {
        if (_library is null) return;
        var library = _library;

        IsLoading = Mods.Count == 0;
        IReadOnlyList<ModItem> items;
        try
        {
            items = await Task.Run(library.Scan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsLoading = false;
            ShowToast("Не удалось прочитать папку Mods: " + ex.Message, Icons.Alert);
            return;
        }
        if (library != _library) return; // пока читали — сменили папку

        // Сохраняем уже загруженные картинки, чтобы список не «мигал».
        var previous = Mods.ToDictionary(m => m.Model.Path, StringComparer.OrdinalIgnoreCase);
        var duplicates = ModLibrary.FindDuplicates(items);

        Mods.Clear();
        foreach (var item in items)
        {
            var vm = new ModItemViewModel(item, this);
            if (previous.TryGetValue(item.Path, out var old))
            {
                vm.Category = old.Category;
                vm.Thumbnail = old.Thumbnail;
            }
            if (duplicates.TryGetValue(item.Path, out var dupMessages))
            {
                vm.IsDuplicate = true;
                vm.Problems = [.. item.Problems, .. dupMessages];
            }
            if (newModNames?.Contains(item.Name) == true) vm.IsNew = true;
            Mods.Add(vm);
        }

        DuplicateCount = duplicates.Count;
        IsLoading = false;

        if (newModNames is { Count: > 0 })
        {
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                foreach (var m in Mods) m.IsNew = false;
            };
            reset.Start();
        }
        RefreshGameState();
        UpdateSummary();
        StartAnalysis();
    }

    /// <summary>В фоне разбираем файлы модов: тип и картинка. Уже разобранное берём из кеша.</summary>
    private void StartAnalysis()
    {
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();
        var pending = Mods.Where(m => m.Category == ModCategory.Unknown || m.Thumbnail is null).ToList();
        if (pending.Count == 0) return;

        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(() =>
        {
            var processed = 0;
            foreach (var vm in pending)
            {
                if (cts.IsCancellationRequested) return;
                var files = vm.Model.Files;
                var stamp = ContentCache.StampOf(files);

                ModContent content;
                ModThumbnail? thumb;
                if (_cache.TryGet(stamp, out var cached))
                {
                    content = cached;
                    thumb = cached.HasThumbnail ? _cache.GetThumbnail(stamp) : null;
                }
                else
                {
                    (content, thumb) = ModAnalyzer.Analyze(files);
                    _cache.Put(stamp, content, thumb);
                }

                var image = thumb is null ? null : ThumbnailFactory.Create(thumb);
                dispatcher.InvokeAsync(() =>
                {
                    vm.Category = content.Category;
                    if (image is not null) vm.Thumbnail = image;
                    if (TypeFilter != TypeFilter.All) RefreshViewSoon();
                }, DispatcherPriority.Background);

                if (++processed % 50 == 0) _cache.Save();
            }
            _cache.Save();
        }, cts.Token);
    }

    private bool _refreshQueued;

    /// <summary>Фильтр по типу пересчитываем не чаще раза в 300 мс, пока идёт фоновый разбор.</summary>
    private void RefreshViewSoon()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _refreshQueued = false;
            RefreshView();
        };
        timer.Start();
    }

    // ---------- Фильтры и сортировка ----------

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private StateFilter _stateFilter = StateFilter.All;
    [ObservableProperty] private TypeFilter _typeFilter = TypeFilter.All;
    [ObservableProperty] private bool _duplicatesOnly;

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnStateFilterChanged(StateFilter value) => RefreshView();
    partial void OnTypeFilterChanged(TypeFilter value)
    {
        OnPropertyChanged(nameof(TypeFilterLabel));
        IsTypeMenuOpen = false;
        RefreshView();
    }
    partial void OnDuplicatesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasDuplicates));
        RefreshView();
    }

    public string TypeFilterLabel => TypeFilter switch
    {
        TypeFilter.Clothing => "Тип: одежда",
        TypeFilter.Hair => "Тип: причёски",
        TypeFilter.Makeup => "Тип: макияж",
        TypeFilter.Objects => "Тип: объекты",
        TypeFilter.Scripts => "Тип: скрипты",
        TypeFilter.Other => "Тип: другое",
        TypeFilter.Tray => "Тип: дома и семьи",
        _ => "Тип: все",
    };

    [ObservableProperty] private bool _isTypeMenuOpen;

    [RelayCommand]
    private void ToggleTypeMenu() => IsTypeMenuOpen = !IsTypeMenuOpen;

    private bool FilterMod(object obj)
    {
        if (obj is not ModItemViewModel mod) return false;
        if (DuplicatesOnly && !mod.IsDuplicate) return false;
        if (StateFilter == StateFilter.Enabled && !mod.IsEnabled) return false;
        if (StateFilter == StateFilter.Disabled && mod.IsEnabled) return false;

        var typeOk = TypeFilter switch
        {
            TypeFilter.Clothing => mod.Category == ModCategory.Clothing,
            TypeFilter.Hair => mod.Category == ModCategory.Hair,
            TypeFilter.Makeup => mod.Category == ModCategory.Makeup,
            TypeFilter.Objects => mod.Category == ModCategory.Objects,
            TypeFilter.Scripts => mod.Category == ModCategory.Scripts || mod.HasScripts,
            TypeFilter.Other => mod.Category is ModCategory.Other or ModCategory.Unknown,
            TypeFilter.Tray => mod.IsTray,
            _ => true,
        };
        if (!typeOk) return false;

        return string.IsNullOrWhiteSpace(SearchText)
               || mod.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void RefreshView()
    {
        ModsView.Refresh();
        UpdateSummary();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = "";
        StateFilter = StateFilter.All;
        TypeFilter = TypeFilter.All;
        DuplicatesOnly = false;
    }

    [RelayCommand]
    private void ShowDuplicates()
    {
        ResetFilters();
        DuplicatesOnly = true;
        Page = AppPage.Library;
    }

    [ObservableProperty] private string _sortBy;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _isSortMenuOpen;

    partial void OnSortByChanged(string value) => OnSortChanged();
    partial void OnSortDescendingChanged(bool value) => OnSortChanged();

    private void OnSortChanged()
    {
        ApplySort();
        _settings.SortBy = SortBy;
        _settings.SortDescending = SortDescending;
        SaveSettings();
        IsSortMenuOpen = false;
        OnPropertyChanged(nameof(SortLabel));
        UpdateSummary();
    }

    public string SortLabel => SortBy switch
    {
        "date" => "По дате",
        "size" => "По размеру",
        _ => "По имени",
    };

    [RelayCommand]
    private void ToggleSortMenu() => IsSortMenuOpen = !IsSortMenuOpen;

    [RelayCommand]
    private void SetSort(string key)
    {
        SortBy = key;
        IsSortMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private void ApplySort()
    {
        using (ModsView.DeferRefresh())
        {
            ModsView.SortDescriptions.Clear();
            var dir = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            var prop = SortBy switch
            {
                "date" => nameof(ModItemViewModel.InstalledAt),
                "size" => nameof(ModItemViewModel.SizeBytes),
                _ => nameof(ModItemViewModel.Name),
            };
            ModsView.SortDescriptions.Add(new SortDescription(prop, dir));
            if (prop != nameof(ModItemViewModel.Name))
                ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItemViewModel.Name), ListSortDirection.Ascending));
        }
    }

    [ObservableProperty] private bool _isGridView;

    partial void OnIsGridViewChanged(bool value)
    {
        _settings.GridView = value;
        SaveSettings();
    }

    // ---------- Выбор нескольких модов ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiSelect), nameof(SelectionText))]
    private int _selectedCount;

    public bool IsMultiSelect => SelectedCount > 1;
    public string SelectionText => "Выбрано " + SelectedCount;

    public void UpdateSelection(IEnumerable<ModItemViewModel> selected) =>
        SelectedCount = selected.Count();

    private List<ModItemViewModel> SelectedMods() => Mods.Where(m => m.IsSelected).ToList();

    /// <summary>Окно снимает выделение и в самом списке (включая строки за пределами экрана).</summary>
    public event Action? ClearSelectionRequested;

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var m in Mods) m.IsSelected = false;
        ClearSelectionRequested?.Invoke();
        SelectedCount = 0;
    }

    // ---------- Сводка ----------

    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _listHeaderText = "";
    [ObservableProperty] private bool _hasNoMods;
    [ObservableProperty] private bool _hasNoResults;
    [ObservableProperty] private string _noResultsTitle = "";
    [ObservableProperty] private bool _showList;

    private void UpdateSummary()
    {
        var enabled = Mods.Count(m => m.IsEnabled);
        SummaryText = $"{Text.Plural(Mods.Count, "мод", "мода", "модов")} · включено {enabled}";

        var visible = ModsView.Cast<object>().Count();
        var arrow = SortDescending ? "↓" : "↑";
        var sortWord = SortBy switch { "date" => "по дате", "size" => "по размеру", _ => "по имени" };
        ListHeaderText = $"{Text.Plural(visible, "мод", "мода", "модов")} · {sortWord} {arrow}";

        HasNoMods = !IsLoading && Mods.Count == 0;
        HasNoResults = !IsLoading && Mods.Count > 0 && visible == 0;
        NoResultsTitle = !string.IsNullOrWhiteSpace(SearchText)
            ? $"Ничего не найдено по запросу «{SearchText.Trim()}»"
            : "Нет модов с такими фильтрами";
        ShowList = !IsLoading && visible > 0;
    }

    partial void OnIsLoadingChanged(bool value) => UpdateSummary();

    // ---------- Статус ----------

    [ObservableProperty] private string _lastActionText = "Готово к работе";

    private void SetLastAction(string text) =>
        LastActionText = $"Последнее действие: {text} · {DateTime.Now:HH:mm}";

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Вызывается при закрытии окна: доделываем отложенные удаления и сохраняем кеш.</summary>
    public void Shutdown()
    {
        _analysisCts?.Cancel();
        FinishPendingDeletes();
        _cache.Save();
    }
}

/// <summary>Иконки, которые нужны в коде (уведомления и диалоги).</summary>
internal static class Icons
{
    private static Geometry Get(string key) => (Geometry)Application.Current.FindResource(key);

    public static Geometry Check => Get("Icon.CircleCheck");
    public static Geometry Info => Get("Icon.Info");
    public static Geometry Alert => Get("Icon.TriangleAlert");
    public static Geometry Error => Get("Icon.CircleAlert");
    public static Geometry Trash => Get("Icon.Trash2");
    public static Geometry FolderSearch => Get("Icon.FolderSearch");
    public static Geometry Gamepad => Get("Icon.Gamepad2");
    public static Geometry Files => Get("Icon.Files");
}
