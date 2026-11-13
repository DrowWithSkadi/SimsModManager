using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

public sealed partial class MainViewModel
{
    public const string RepoUrl = "https://github.com/DrowWithSkadi/SimsModManager";

    public string VersionText { get; } = "v" + (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    // Значения переключателей «Моды в игре». Флаг нужен, чтобы при чтении файла не записывать его обратно.
    private bool _loadingSettings;

    [ObservableProperty] private bool _gameModsEnabled = true;
    [ObservableProperty] private bool _gameScriptsEnabled = true;
    [ObservableProperty] private bool _optionsFileFound;
    [ObservableProperty] private bool _autoClearCache;
    [ObservableProperty] private bool _deleteToRecycleBin;
    [ObservableProperty] private bool _showSplash;
    [ObservableProperty] private bool _showMascot = true;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private bool _folderLooksValid;

    // ---------- Оформление ----------

    [ObservableProperty] private string _theme = Services.ThemeService.Current;

    public bool GlassSupported => Services.ThemeService.GlassSupported;

    [RelayCommand]
    private void SetTheme(string name) => Theme = name;

    partial void OnThemeChanged(string value)
    {
        if (_loadingSettings) return;
        Services.ThemeService.Apply(value);
        _settings.Theme = Services.ThemeService.Current;
        SaveSettings();
        OnPropertyChanged(nameof(ThemeLabel));
    }

    public string ThemeLabel => Theme switch
    {
        Services.ThemeService.Light => L.T("Светлая · слоновая кость"),
        Services.ThemeService.Glass => L.T("Стекло · размытие Windows 11"),
        _ => L.T("Тёмная · графит"),
    };


    private void InitSettingsPage()
    {
        _loadingSettings = true;
        Theme = Services.ThemeService.Current;
        AutoClearCache = _settings.AutoClearCache;
        DeleteToRecycleBin = _settings.DeleteToRecycleBin;
        ShowSplash = _settings.ShowSplash;
        ShowMascot = _settings.ShowMascot;
        Language = _settings.Language is L.Russian or L.English ? _settings.Language : "auto";
        _loadingSettings = false;
    }

    private void RefreshSettingsPage()
    {
        _loadingSettings = true;
        if (_library is not null)
        {
            var state = GameOptions.ReadModSettings(_library.Paths);
            OptionsFileFound = state.OptionsFileFound;
            GameModsEnabled = state.ModsEnabled;
            GameScriptsEnabled = state.ScriptModsEnabled;
            FolderLooksValid = GameLocator.LooksLikeSimsFolder(_library.Paths.Root);
        }
        _loadingSettings = false;
    }

    partial void OnGameModsEnabledChanged(bool value)
    {
        if (!_loadingSettings) _ = ApplyGameOptionAsync(mods: value, scripts: null);
    }

    partial void OnGameScriptsEnabledChanged(bool value)
    {
        if (!_loadingSettings) _ = ApplyGameOptionAsync(mods: null, scripts: value);
    }

    private async Task ApplyGameOptionAsync(bool? mods, bool? scripts)
    {
        if (_library is null) return;
        if (!await EnsureGameClosedAsync(L.T("изменить настройки")))
        {
            RefreshGameState();
            return;
        }
        try
        {
            GameOptions.SetModSettings(_library.Paths, mods, scripts);
            ShowToast(L.T("Настройки игры сохранены"), Icons.Check);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(L.T("Не удалось изменить настройки"), L.T("Файл Options.ini не получилось сохранить."), ex);
        }
        RefreshGameState();
    }

    partial void OnAutoClearCacheChanged(bool value)
    {
        if (_loadingSettings) return;
        _settings.AutoClearCache = value;
        SaveSettings();
    }

    partial void OnShowMascotChanged(bool value)
    {
        if (_loadingSettings) return;
        _settings.ShowMascot = value;
        SaveSettings();
    }

    partial void OnShowSplashChanged(bool value)
    {
        if (_loadingSettings) return;
        _settings.ShowSplash = value;
        SaveSettings();
    }

    partial void OnDeleteToRecycleBinChanged(bool value)
    {
        if (_loadingSettings) return;
        _settings.DeleteToRecycleBin = value;
        SaveSettings();
        if (_library is not null && SimsFolder is not null)
            _library = new ModLibrary(new SimsPaths(SimsFolder), CreateDeleter());
    }

    [RelayCommand]
    private void SetDeleteMode(string mode) => DeleteToRecycleBin = mode != "permanent";

    [RelayCommand]
    private void AutoFindFolder() => RetrySearch();

    [RelayCommand]
    private void OpenGitHub()
    {
        try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
        catch (Exception) { /* нет браузера — не страшно */ }
    }

    /// <summary>
    /// Установленная версия обновляется сама через Velopack (скачивает новую сборку с GitHub
    /// и перезапускается). Если программа запущена без установки — просто сообщаем о новой версии.
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdates()
    {
        try
        {
            // Сначала берём файлы тестовой сборки напрямую (github.com/.../releases/download/dev/) —
            // это не API GitHub, у него нет лимитов и он реже отвечает 503. Если не вышло — через API.
            var mgr = new Velopack.UpdateManager(new Velopack.Sources.SimpleWebSource(RepoUrl + "/releases/download/dev/"));
            if (mgr.IsInstalled)
            {
                IsCheckingUpdates = true;
                UpdateStatus = L.T("Проверяем…");
                Velopack.UpdateInfo? info;
                try
                {
                    info = await mgr.CheckForUpdatesAsync();
                }
                catch (Exception)
                {
                    mgr = new Velopack.UpdateManager(new Velopack.Sources.GithubSource(RepoUrl, null, true));
                    info = await mgr.CheckForUpdatesAsync();
                }
                if (info is null)
                {
                    UpdateStatus = L.F("У вас последняя версия. Последняя проверка: сегодня, {0:HH:mm}", DateTime.Now);
                    return;
                }

                var version = info.TargetFullRelease.Version.ToString();
                var answer = await ShowDialogAsync(new DialogViewModel
                {
                    Title = L.T("Доступно обновление"),
                    Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.Download"),
                    Message = L.F("Новая версия {0}. Программа скачает её и перезапустится — моды и настройки не затрагиваются.", version),
                    PrimaryText = L.T("Обновить"),
                    SecondaryText = L.T("Позже"),
                });
                if (answer != DialogResult.Primary)
                {
                    UpdateStatus = L.F("Доступна версия {0}", version);
                    return;
                }

                UpdateStatus = L.T("Скачиваем обновление…");
                await mgr.DownloadUpdatesAsync(info, p => UpdateStatus = L.F("Скачиваем обновление… {0}%", p));
                Shutdown();
                mgr.ApplyUpdatesAndRestart(info);
                return;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = ex is HttpRequestException or TaskCanceledException
                ? L.T("GitHub сейчас не отвечает. Попробуйте через пару минут — или скачайте установщик на странице GitHub.")
                : L.T("Не удалось обновиться: ") + ex.Message;
            IsCheckingUpdates = false;
            return;
        }
        finally
        {
            IsCheckingUpdates = false;
        }

        IsCheckingUpdates = true;
        UpdateStatus = L.T("Проверяем…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SimsModManager");
            using var response = await http.GetAsync("https://api.github.com/repos/DrowWithSkadi/SimsModManager/releases/latest");
            var when = L.F("Последняя проверка: сегодня, {0:HH:mm}", DateTime.Now);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                UpdateStatus = L.T("Стабильных версий пока нет. ") + when;
                return;
            }
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = tag.TrimStart('v');
            var current = VersionText.TrimStart('v');
            UpdateStatus = Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c
                ? L.F("Доступна версия {0} — откройте GitHub, чтобы скачать. {1}", tag, when)
                : L.F("У вас последняя версия. {0}", when);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            UpdateStatus = L.T("Не удалось проверить: нет связи с GitHub.");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }
}
