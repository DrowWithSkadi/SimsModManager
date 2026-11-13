using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.App.Overlay;
using SimsModManager.Core;
using SimsModManager.Core.Helper;
using SimsModManager.Core.Saves;

namespace SimsModManager.App.ViewModels;

/// <summary>Оверлей поверх игры, мод-помощник и точки сохранения.</summary>
public sealed partial class MainViewModel
{
    private OverlayHost? _overlay;
    private readonly object _checkpointLock = new();
    private CheckpointManager? _checkpoints;
    private string? _checkpointsRoot;

    /// <summary>Файл помощника, который идёт вместе с программой.</summary>
    private static string BundledHelper => Path.Combine(AppContext.BaseDirectory, "Assets", "Helper", HelperMod.FileName);

    [ObservableProperty] private bool _overlayEnabled;
    [ObservableProperty] private string _overlayHotkey = "F9";
    [ObservableProperty] private bool _overlayPausesGame;
    [ObservableProperty] private bool _overlayHideFromCapture;
    [ObservableProperty] private string? _overlayProblem;
    [ObservableProperty] private string _helperStatusText = "";
    [ObservableProperty] private bool _isRecordingHotkey;

    [ObservableProperty] private string _checkpointMaxAgeDays = "";
    [ObservableProperty] private string _checkpointMaxCount = "";
    [ObservableProperty] private string _checkpointMaxTotalMegabytes = "";
    [ObservableProperty] private string _checkpointStorageText = "";

    /// <summary>Вызывается из любого потока (оверлей живёт в своём).</summary>
    private CheckpointManager? GetCheckpointManager()
    {
        var paths = _library?.Paths;
        if (paths is null) return null;
        lock (_checkpointLock)
        {
            if (_checkpoints is null || _checkpointsRoot != paths.Root)
            {
                _checkpoints = new CheckpointManager(paths, new CheckpointStore(CheckpointStore.DefaultFolder),
                    new HelperBridge(HelperMod.BridgeFolder(paths)));
                _checkpointsRoot = paths.Root;
            }
            _checkpoints.Retention = _settings.CheckpointRetention;
            _checkpoints.LaunchGame = () =>
            {
                var game = GameLauncher.Find(_settings.GameExe);
                if (game is null) return false;
                GameLauncher.Launch(game);
                return true;
            };
            return _checkpoints;
        }
    }

    private void InitOverlay()
    {
        _loadingSettings = true;
        OverlayEnabled = _settings.OverlayEnabled;
        OverlayHotkey = Hotkey.Parse(_settings.OverlayHotkey).ToString();
        OverlayPausesGame = _settings.OverlayPausesGame;
        OverlayHideFromCapture = _settings.OverlayHideFromCapture;
        CheckpointMaxAgeDays = _settings.CheckpointMaxAgeDays?.ToString() ?? "";
        CheckpointMaxCount = _settings.CheckpointMaxCount?.ToString() ?? "";
        CheckpointMaxTotalMegabytes = _settings.CheckpointMaxTotalMegabytes?.ToString() ?? "";
        _loadingSettings = false;

        EnsureHelperInstalled();
        try
        {
            _overlay = new OverlayHost(OverlaySettings.From(_settings), GetCheckpointManager);
            _overlay.HotkeyProblemChanged += problem =>
                Application.Current?.Dispatcher.BeginInvoke(() => OverlayProblem = problem);
            _overlay.Start();
        }
        catch (Exception ex)
        {
            OverlayProblem = L.T("Оверлей не запустился: ") + ex.Message;
        }
    }

    /// <summary>Ставит или обновляет помощника. Он работает, только если в игре включены скриптовые моды.</summary>
    private void EnsureHelperInstalled()
    {
        if (_library is null) return;
        try
        {
            if (_settings.OverlayEnabled) HelperMod.Install(_library.Paths, BundledHelper);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Игра держит файл открытым — обновим при следующем запуске программы
        }
        RefreshHelperStatus();
    }

    /// <summary>Строка «Помощник: …» в настройках. Вызывается вместе с проверкой «игра запущена».</summary>
    private void RefreshHelperStatus()
    {
        if (_library is null) { HelperStatusText = ""; return; }
        if (!HelperMod.IsInstalled(_library.Paths))
        {
            HelperStatusText = File.Exists(BundledHelper) ? L.T("Не установлен") : L.T("Не входит в эту сборку");
            return;
        }
        var status = new HelperBridge(HelperMod.BridgeFolder(_library.Paths)).ReadStatus();
        HelperStatusText = status.State switch
        {
            HelperState.Live => L.T("Работает в игре"),
            HelperState.Menu => L.T("Работает · игра в главном меню"),
            HelperState.Starting => L.T("Игра загружается"),
            _ when IsGameRunning && !GameScriptsEnabled => L.T("Скриптовые моды выключены — помощник не работает"),
            _ when IsGameRunning => L.T("Не отвечает — перезапусти игру"),
            _ => L.T("Установлен · заработает при запуске игры"),
        };
        CheckpointStorageText = StorageSummary();
    }

    private static string StorageSummary()
    {
        try
        {
            var store = new CheckpointStore(CheckpointStore.DefaultFolder);
            var all = store.List();
            if (all.Count == 0) return L.T("Точек пока нет");
            return L.F("Точек: {0} · {1:0.#} МБ · закреплено: {2}", all.Count, all.Sum(c => c.SizeBytes) / 1024.0 / 1024.0, all.Count(c => c.Pinned));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void ApplyOverlaySettings()
    {
        if (_loadingSettings) return;
        _settings.OverlayEnabled = OverlayEnabled;
        _settings.OverlayHotkey = OverlayHotkey;
        _settings.OverlayPausesGame = OverlayPausesGame;
        _settings.OverlayHideFromCapture = OverlayHideFromCapture;
        SaveSettings();
        _overlay?.Apply(OverlaySettings.From(_settings));
        EnsureHelperInstalled();
    }

    partial void OnOverlayEnabledChanged(bool value) => ApplyOverlaySettings();
    partial void OnOverlayPausesGameChanged(bool value) => ApplyOverlaySettings();
    partial void OnOverlayHideFromCaptureChanged(bool value) => ApplyOverlaySettings();

    [RelayCommand]
    private void RecordHotkey() => IsRecordingHotkey = true;

    /// <summary>Нажатие в поле «Клавиша оверлея». true — нажатие принято.</summary>
    public bool TrySetHotkey(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        if (key == System.Windows.Input.Key.Escape)
        {
            IsRecordingHotkey = false;
            return true;
        }
        var hotkey = Hotkey.FromKeyPress(key, modifiers);
        if (hotkey is null) return false;
        OverlayHotkey = hotkey.ToString();
        IsRecordingHotkey = false;
        OverlayProblem = null;
        ApplyOverlaySettings();
        return true;
    }

    partial void OnCheckpointMaxAgeDaysChanged(string value) => ApplyRetention();
    partial void OnCheckpointMaxCountChanged(string value) => ApplyRetention();
    partial void OnCheckpointMaxTotalMegabytesChanged(string value) => ApplyRetention();

    private static int? ParsePositive(string text) =>
        int.TryParse(text.Trim(), out var n) && n > 0 ? n : null;

    private void ApplyRetention()
    {
        if (_loadingSettings) return;
        _settings.CheckpointMaxAgeDays = ParsePositive(CheckpointMaxAgeDays);
        _settings.CheckpointMaxCount = ParsePositive(CheckpointMaxCount);
        _settings.CheckpointMaxTotalMegabytes = ParsePositive(CheckpointMaxTotalMegabytes);
        SaveSettings();
    }

    [RelayCommand]
    private void OpenCheckpointsFolder()
    {
        Directory.CreateDirectory(CheckpointStore.DefaultFolder);
        System.Diagnostics.Process.Start("explorer.exe", CheckpointStore.DefaultFolder);
    }
}
