using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---------- Перетаскивание ----------

    [ObservableProperty] private bool _isDragOver;
    [ObservableProperty] private string _dragCountText = "";

    public void SetDragFiles(int count)
    {
        DragCountText = Text.Plural(count, "файл", "файла", "файлов");
        IsDragOver = count > 0 && !IsInstalling && HasGame;
    }

    // ---------- Установка ----------

    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallPercentText))]
    private double _installPercent;

    public string InstallPercentText => $"{InstallPercent:0}%";
    [ObservableProperty] private string _installCountText = "";
    [ObservableProperty] private string _installCurrent = "";

    public ObservableCollection<InstallResultViewModel> LastResults { get; } = [];
    [ObservableProperty] private string _resultSummary = "";

    [RelayCommand]
    private async Task BrowseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
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
        IsDragOver = false;
        if (_installer is null)
        {
            await ShowDialogAsync(new DialogViewModel
            {
                Title = "Сначала укажите папку игры",
                Icon = Icons.FolderSearch,
                Message = "Без неё менеджер не знает, куда ставить моды.",
                PrimaryText = "Понятно",
            });
            return;
        }
        if (IsInstalling || sources.Count == 0) return;

        IsInstalling = true;
        InstallPercent = 0;
        InstallCountText = $"0 из {sources.Count}";
        InstallCurrent = "";

        IReadOnlyList<InstallResult> results;
        try
        {
            var installer = _installer;
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallPercent = p.Total == 0 ? 100 : 100.0 * p.Index / p.Total;
                InstallCountText = $"{Math.Min(p.Index + 1, p.Total)} из {p.Total}";
                if (p.CurrentName.Length > 0) InstallCurrent = p.CurrentName;
            });
            // Небольшая минимальная длительность, чтобы окно установки не «мигало».
            var minimum = Task.Delay(500);
            results = await Task.Run(() => installer.Install(sources, progress));
            InstallPercent = 100;
            await minimum;
        }
        catch (Exception ex)
        {
            IsInstalling = false;
            await ShowErrorAsync("Что-то пошло не так", "Не удалось завершить установку. Уже установленные моды не затронуты.", ex);
            return;
        }

        LastResults.Clear();
        foreach (var r in results) LastResults.Add(new InstallResultViewModel(r));

        var ok = results.Count(r => r.Succeeded);
        ResultSummary = $"{Text.Plural(results.Count, "файл обработан", "файла обработано", "файлов обработано")} · "
                        + $"{Text.Plural(ok, "мод готов", "мода готовы", "модов готовы")} к игре";

        var installed = results.Where(r => r.Succeeded && r.ModName is not null).Select(r => r.ModName!).ToHashSet();
        if (ok > 0)
        {
            SetLastAction("установлено " + Text.Plural(ok, "мод", "мода", "модов"));
            if (_settings.AutoClearCache && _library is not null && !GameOptions.IsGameRunning())
            {
                try { GameOptions.ClearThumbnailCache(_library.Paths); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        IsInstalling = false;
        ResetFilters();
        Page = AppPage.InstallResult;
        await RefreshAsync(installed);

        if (GameOptions.IsGameRunning())
            ShowToast("Игра запущена — изменения применятся после перезапуска", Icons.Info, seconds: 6);
    }

    [RelayCommand]
    private void CloseResults()
    {
        LastResults.Clear();
        Page = AppPage.Library;
    }
}

public sealed class InstallResultViewModel(InstallResult result)
{
    public string Title => Path.GetFileName(result.Source);
    public InstallStatus Status => result.Status;
    public bool IsError => result.Status == InstallStatus.Failed;

    public string Message => result.Status switch
    {
        InstallStatus.Installed => "Установлен · " + Text.Plural(result.FileCount, "файл", "файла", "файлов"),
        InstallStatus.Updated => "Обновлён · " + Text.Plural(result.FileCount, "файл", "файла", "файлов"),
        InstallStatus.Skipped => result.Message ?? "Пропущен",
        _ => result.Message ?? "Ошибка",
    };

    public Geometry Icon => result.Status switch
    {
        InstallStatus.Installed => Icons.Check,
        InstallStatus.Updated => (Geometry)System.Windows.Application.Current.FindResource("Icon.RefreshCw"),
        InstallStatus.Skipped => (Geometry)System.Windows.Application.Current.FindResource("Icon.CircleMinus"),
        _ => Icons.Error,
    };
}
