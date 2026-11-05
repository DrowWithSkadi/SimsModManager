using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SimsModManager.Core;
using SimsModManager.Core.Archives;

namespace SimsModManager.App.ViewModels;

public enum ModFilter { All, Enabled, Disabled }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IArchiveExtractor _extractor = new SharpCompressExtractor();
    private ModLibrary? _library;
    private ModInstaller? _installer;

    public MainViewModel()
    {
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMod;
        InitGameFolder();
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ICollectionView ModsView { get; }
    public ObservableCollection<InstallResultViewModel> LastResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    private string? _simsFolder;

    public bool HasGame => _library is not null;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ModFilter _filter = ModFilter.All;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _optionsWarning;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _hasNoMods;

    partial void OnSearchTextChanged(string value) => ModsView.Refresh();
    partial void OnFilterChanged(ModFilter value) => ModsView.Refresh();

    private bool FilterMod(object obj)
    {
        if (obj is not ModItemViewModel mod) return false;
        if (Filter == ModFilter.Enabled && !mod.IsEnabled) return false;
        if (Filter == ModFilter.Disabled && mod.IsEnabled) return false;
        return string.IsNullOrWhiteSpace(SearchText)
               || mod.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    // ---------- Папка игры ----------

    private void InitGameFolder()
    {
        var folder = _settings.SimsFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            folder = GameLocator.FindSimsFolder();
            if (folder is not null)
            {
                _settings.SimsFolder = folder;
                TrySaveSettings();
            }
        }
        UseFolder(folder);
    }

    private void UseFolder(string? folder)
    {
        if (folder is null)
        {
            _library = null;
            _installer = null;
            SimsFolder = null;
            Mods.Clear();
            return;
        }

        var paths = new SimsPaths(folder);
        _library = new ModLibrary(paths, new RecycleBinDeleter());
        _installer = new ModInstaller(paths, _extractor);
        SimsFolder = paths.Root;
        Refresh();
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку The Sims 4 (внутри «Документы\\Electronic Arts»)",
            InitialDirectory = SimsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;

        var folder = dialog.FolderName;
        // Если выбрали саму папку Mods — поднимаемся на уровень выше.
        if (Path.GetFileName(folder).Equals("Mods", StringComparison.OrdinalIgnoreCase))
            folder = Path.GetDirectoryName(folder)!;

        if (!GameLocator.LooksLikeSimsFolder(folder))
        {
            var answer = MessageBox.Show(
                "Эта папка не похожа на папку The Sims 4 (в ней нет Mods, saves или Options.ini).\n\nВсё равно использовать её?",
                "Папка игры", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        _settings.SimsFolder = folder;
        TrySaveSettings();
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
        if (mod.Model.IsFolder) OpenInExplorer(mod.Model.Path);
        else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{mod.Model.Path}\"") { UseShellExecute = true });
    }

    private static void OpenInExplorer(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // ---------- Список ----------

    [RelayCommand]
    private void Refresh()
    {
        if (_library is null) return;

        Mods.Clear();
        try
        {
            foreach (var item in _library.Scan())
                Mods.Add(new ModItemViewModel(item, this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Не удалось прочитать папку Mods: " + ex.Message;
        }

        var state = GameOptions.ReadModSettings(_library.Paths);
        OptionsWarning = state.AllGood
            ? null
            : !state.ModsEnabled
                ? "Моды выключены в настройках игры — она их не загрузит."
                : "Скриптовые моды выключены в настройках игры — моды со скриптами работать не будут.";

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var enabled = Mods.Count(m => m.IsEnabled);
        HasNoMods = Mods.Count == 0;
        SummaryText = $"{Text.Plural(Mods.Count, "мод", "мода", "модов")} · включено {enabled}";
    }

    internal ModItem? TrySetEnabled(ModItem mod, bool enabled)
    {
        if (_library is null) return null;
        try
        {
            var updated = _library.SetEnabled(mod, enabled);
            StatusText = enabled ? $"«{mod.Name}» включён" : $"«{mod.Name}» выключен";
            // Обновляем список после того, как переключатель закончит своё изменение.
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ModsView.Refresh();
                UpdateSummary();
            });
            return updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message + GameRunningHint(), "Не получилось", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    [RelayCommand]
    private void DeleteMod(ModItemViewModel? mod)
    {
        if (mod is null || _library is null) return;

        var answer = MessageBox.Show(
            $"Удалить мод «{mod.Name}»?\n\nФайлы будут перемещены в корзину.",
            "Удаление мода", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _library.Delete(mod.Model);
            Mods.Remove(mod);
            UpdateSummary();
            StatusText = $"«{mod.Name}» удалён";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            MessageBox.Show("Не удалось удалить: " + ex.Message + GameRunningHint(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Установка ----------

    [RelayCommand]
    private async Task BrowseFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите архивы или файлы модов",
            Multiselect = true,
            Filter = "Моды и архивы|*.zip;*.rar;*.7z;*.package;*.ts4script|Все файлы|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog() == true)
            await InstallAsync(dialog.FileNames);
    }

    public async Task InstallAsync(IReadOnlyList<string> sources)
    {
        if (_installer is null)
        {
            MessageBox.Show("Сначала укажите папку игры.", "Папка игры не найдена", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (IsBusy || sources.Count == 0) return;

        IsBusy = true;
        LastResults.Clear();
        try
        {
            var installer = _installer;
            var progress = new Progress<string>(name => StatusText = $"Устанавливаю: {name}…");
            var results = await Task.Run(() => installer.Install(sources, progress));

            foreach (var r in results) LastResults.Add(new InstallResultViewModel(r));

            var ok = results.Count(r => r.Status == InstallStatus.Installed);
            var upd = results.Count(r => r.Status == InstallStatus.Updated);
            var bad = results.Count(r => !r.Succeeded);
            var parts = new List<string>();
            if (ok > 0) parts.Add($"установлено {ok}");
            if (upd > 0) parts.Add($"обновлено {upd}");
            if (bad > 0) parts.Add($"с ошибками {bad}");
            StatusText = "Готово: " + string.Join(", ", parts)
                         + (GameOptions.IsGameRunning() ? ". Игра запущена — перезапустите её, чтобы увидеть изменения." : "");
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void ClearResults() => LastResults.Clear();

    // ---------- Настройки игры ----------

    [RelayCommand]
    private void FixOptions()
    {
        if (_library is null) return;
        if (GameOptions.IsGameRunning())
        {
            MessageBox.Show("Закройте игру и нажмите ещё раз — пока она запущена, она перезапишет настройки.",
                "Игра запущена", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            GameOptions.EnableMods(_library.Paths);
            StatusText = "Моды и скрипты включены в настройках игры";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Не удалось изменить Options.ini: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }

    [RelayCommand]
    private void ClearCache()
    {
        if (_library is null) return;
        if (GameOptions.IsGameRunning())
        {
            MessageBox.Show("Закройте игру, чтобы очистить кэш.", "Игра запущена", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            StatusText = GameOptions.ClearThumbnailCache(_library.Paths)
                ? "Кэш миниатюр очищен — игра создаст его заново при запуске"
                : "Кэш уже пуст";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Не удалось очистить кэш: " + ex.Message;
        }
    }

    private static string GameRunningHint() =>
        GameOptions.IsGameRunning() ? "\n\nПохоже, игра запущена — закройте её и попробуйте снова." : "";

    private void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* не критично */ }
    }
}

public sealed class InstallResultViewModel(InstallResult result)
{
    public string Title => result.ModName ?? Path.GetFileName(result.Source);
    public bool Succeeded => result.Succeeded;

    public string Message => result.Status switch
    {
        InstallStatus.Installed => "Установлен · " + ViewModels.Text.Plural(result.FileCount, "файл", "файла", "файлов"),
        InstallStatus.Updated => "Обновлён · " + ViewModels.Text.Plural(result.FileCount, "файл", "файла", "файлов"),
        InstallStatus.Skipped => "Пропущен: " + result.Message,
        _ => "Ошибка: " + result.Message,
    };
}
