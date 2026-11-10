using System.IO;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

/// <summary>Кнопка «Играть»: проверка модов → очистка кэша → бэкап сохранений → запуск.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private string _launchStep = "";
    [ObservableProperty] private double _launchProgress;

    /// <summary>Кнопка видна в библиотеке и на проверке модов (на других страницах внизу свои кнопки).</summary>
    public bool ShowPlayButton => HasGame && !IsMultiSelect && Page is AppPage.Library or AppPage.Health or AppPage.InstallResult;

    /// <summary>Коробок «работает» и при установке, и при подготовке к запуску — а потом радуется.</summary>
    public bool MascotBusy => IsInstalling || IsLaunching;

    private bool _playHooked;

    private void HookPlay()
    {
        if (_playHooked) return;
        _playHooked = true;
        PropertyChanged += OnPropertyChangedForPlay;
        RestoreModSettingsIfGameClosed();
    }

    private void OnPropertyChangedForPlay(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Page) or nameof(SimsFolder) or nameof(IsMultiSelect):
                OnPropertyChanged(nameof(ShowPlayButton));
                break;
            case nameof(IsInstalling) or nameof(IsLaunching):
                OnPropertyChanged(nameof(MascotBusy));
                break;
            case nameof(IsGameRunning) when !IsGameRunning:
                RestoreModSettingsIfGameClosed();
                if (Bisect is { IsFinished: false, LaunchedAt: not null }) _ = AutoAnswerBisectAsync();
                break;
        }
    }

    private void Step(string text, double progress)
    {
        LaunchStep = text;
        LaunchProgress = progress;
    }

    /// <param name="mode">null — обычный запуск; "nomods" — без модов; "noscripts" — без скриптов.</param>
    [RelayCommand]
    private async Task LaunchGame(string? mode)
    {
        if (IsLaunching || _library is null) return;
        if (GameOptions.IsGameRunning())
        {
            IsGameRunning = true;
            ShowToast("Игра уже запущена", Icons.Gamepad);
            return;
        }
        var paths = _library.Paths;
        IsLaunching = true;
        try
        {
            // 1. Проверка модов
            Step("Проверяю моды", 0.15);
            await Task.Delay(350);
            if (mode is null && !await ConfirmModsBeforeLaunch(paths)) return;

            // 2. Кэш
            Step("Чищу кэш", 0.38);
            try { GameOptions.ClearThumbnailCache(paths); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            await Task.Delay(300);

            // 3. Бэкап сохранений
            Step("Делаю бэкап сохранений", 0.6);
            try
            {
                var started = DateTime.Now;
                await Task.Run(() => SaveBackup.Create(paths, SaveBackup.DefaultFolder));
                var left = TimeSpan.FromMilliseconds(400) - (DateTime.Now - started);
                if (left > TimeSpan.Zero) await Task.Delay(left);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ShowToast("Не получилось сделать бэкап сохранений", Icons.Alert);
            }

            // 4. Запуск
            Step("Запускаю игру", 0.85);
            var game = GameLauncher.Find(_settings.GameExe) ?? await AskForGameExe();
            if (game is null) return;

            if (mode is "nomods" or "noscripts") TemporarilyDisable(paths, mode);
            MarkBisectLaunch();

            try
            {
                GameLauncher.Launch(game);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                RestoreModSettingsIfGameClosed(force: true);
                await ShowDialogAsync(new DialogViewModel
                {
                    Title = "Не получилось запустить игру",
                    Icon = Icons.Error,
                    Message = ex.Message,
                    PathText = game.Exe,
                    PrimaryText = "Понятно",
                });
                return;
            }

            // Ждём, пока игра появится (EA app или Steam могут думать до минуты)
            Step("Игра запускается…", 1);
            var until = DateTime.Now.AddSeconds(90);
            while (DateTime.Now < until && !GameOptions.IsGameRunning()) await Task.Delay(1000);
            IsGameRunning = GameOptions.IsGameRunning();
        }
        finally
        {
            IsLaunching = false;
            LaunchProgress = 0;
        }
    }

    /// <summary>Моды выключены в игре или скрипты могли сломаться после патча — спрашиваем до запуска.</summary>
    private async Task<bool> ConfirmModsBeforeLaunch(SimsPaths paths)
    {
        var options = GameOptions.ReadModSettings(paths);
        if (options.OptionsFileFound && (!options.ModsEnabled || !options.ScriptModsEnabled))
        {
            var answer = await ShowDialogAsync(new DialogViewModel
            {
                Title = options.ModsEnabled ? "Скриптовые моды выключены в игре" : "Моды выключены в настройках игры",
                Icon = Icons.Alert,
                Message = "Игра запустится без них. Включить перед запуском?",
                PrimaryText = "Включить и играть",
                ExtraText = "Играть так",
                SecondaryText = "Отмена",
            });
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Primary)
            {
                try { GameOptions.EnableMods(paths); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                RefreshGameState();
            }
        }

        if (HasGameUpdate)
        {
            var answer = await ShowDialogAsync(new DialogViewModel
            {
                Title = "Игра недавно обновилась",
                Icon = Icons.Alert,
                Message = GameUpdateText + " Они могут сломать игру — их лучше выключить, пока авторы не выпустят обновления.",
                PrimaryText = "Выключить их и играть",
                ExtraText = "Всё равно играть",
                SecondaryText = "Отмена",
            });
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Primary) await DisableOutdatedScripts();
        }
        return true;
    }

    private async Task<GameLauncher.GameInstall?> AskForGameExe()
    {
        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = "Не нашёл, где установлена игра",
            Icon = Icons.FolderSearch,
            Message = "Покажите файл TS4_x64.exe — обычно он лежит в папке игры, в Game\\Bin. Запомню и больше не спрошу.",
            PrimaryText = "Указать файл",
            SecondaryText = "Отмена",
        });
        if (answer != DialogResult.Primary) return null;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Где лежит TS4_x64.exe?",
            Filter = "The Sims 4|TS4_x64.exe;TS4.exe|Программы|*.exe",
        };
        if (dialog.ShowDialog() != true) return null;
        _settings.GameExe = dialog.FileName;
        SaveSettings();
        ScanPacks();
        return GameLauncher.From(dialog.FileName);
    }

    /// <summary>«Играть без модов/скриптов»: выключаем в Options.ini и запоминаем, как было.</summary>
    private void TemporarilyDisable(SimsPaths paths, string mode)
    {
        var before = GameOptions.ReadModSettings(paths);
        if (!before.OptionsFileFound) return;
        _settings.RestoreModsEnabled = before.ModsEnabled;
        _settings.RestoreScriptsEnabled = before.ScriptModsEnabled;
        SaveSettings();
        try
        {
            if (mode == "nomods") GameOptions.SetModSettings(paths, mods: false, scripts: null);
            else GameOptions.SetModSettings(paths, mods: null, scripts: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Игра закрылась после «без модов» — возвращаем настройки как были.</summary>
    private void RestoreModSettingsIfGameClosed(bool force = false)
    {
        if (_settings.RestoreModsEnabled is null && _settings.RestoreScriptsEnabled is null) return;
        if (_library is null) return;
        if (!force && (IsLaunching || GameOptions.IsGameRunning())) return;
        try
        {
            GameOptions.SetModSettings(_library.Paths, _settings.RestoreModsEnabled, _settings.RestoreScriptsEnabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        _settings.RestoreModsEnabled = null;
        _settings.RestoreScriptsEnabled = null;
        SaveSettings();
        RefreshGameState();
    }

    [RelayCommand]
    private void OpenSaveBackups()
    {
        Directory.CreateDirectory(SaveBackup.DefaultFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SaveBackup.DefaultFolder) { UseShellExecute = true })?.Dispose();
    }
}
