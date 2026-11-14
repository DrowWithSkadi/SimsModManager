using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SimsModManager.Core;
using SimsModManager.Core.Saves;

namespace SimsModManager.App.Overlay;

public sealed record OverlaySettings(bool Enabled, string Hotkey, bool PausesGame, bool HideFromCapture)
{
    public static OverlaySettings From(AppSettings s) => new(s.OverlayEnabled, s.OverlayHotkey, s.OverlayPausesGame, s.OverlayHideFromCapture);
}

/// <summary>
/// Оверлей живёт в собственном потоке со своим диспетчером: что бы ни делало главное окно
/// (сканирование тысяч модов, анализ), оверлей открывается мгновенно и не подтормаживает.
/// Окно создаётся заранее и только показывается/прячется.
///
/// Клавиша регистрируется через RegisterHotKey и только пока игра на переднем плане —
/// в остальное время F9 свободна для других программ.
/// </summary>
public sealed class OverlayHost
{
    private const int HotkeyId = 0x534D; // «SM»
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Func<CheckpointManager?> _manager;
    private readonly string _logPath = Core.AppIdentity.Roaming("overlay.log");

    private Dispatcher? _dispatcher;
    private OverlayWindow? _window;
    private OverlayViewModel? _viewModel;
    private HwndSource? _messages;
    private IntPtr _foregroundHook;
    private Native.WinEventProc? _hookProc; // держим ссылку, иначе сборщик мусора уберёт обработчик
    private readonly Dictionary<uint, bool> _gameProcesses = [];
    private OverlaySettings _settings;
    private Hotkey _hotkey = Hotkey.Default;
    private bool _registered;
    private IntPtr _gameWindow;

    public OverlayHost(OverlaySettings settings, Func<CheckpointManager?> manager)
    {
        _settings = settings;
        _manager = manager;
    }

    /// <summary>Не удалось занять клавишу (её использует другая программа). Вызывается в потоке оверлея.</summary>
    public event Action<string?>? HotkeyProblemChanged;

    public string? HotkeyProblem { get; private set; }

    public void Start()
    {
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _dispatcher.UnhandledException += OnUnhandled;
                Build();
            }
            catch (Exception ex)
            {
                Log("Не удалось запустить оверлей", ex);
            }
            finally
            {
                ready.Set();
            }
            Dispatcher.Run();
        })
        {
            Name = "Overlay",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Apply(OverlaySettings settings) => Post(() =>
    {
        _settings = settings;
        UpdateHotkey();
        ApplyCaptureSetting();
        if (!settings.Enabled) HideOverlay(returnFocus: true);
    });

    public void Stop() => Post(() =>
    {
        Unregister();
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        _window?.CloseForReal();
        _messages?.Dispose();
        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
    });

    private void Post(Action action)
    {
        try { _dispatcher?.BeginInvoke(action); }
        catch (Exception ex) { Log("Оверлей недоступен", ex); }
    }

    private void Build()
    {
        _viewModel = new OverlayViewModel(_manager, () => _settings.PausesGame, () => HideOverlay(returnFocus: true));
        _window = new OverlayWindow { DataContext = _viewModel };
        _window.CloseRequested += () => HideOverlay(returnFocus: true);
        var handle = new WindowInteropHelper(_window).EnsureHandle();
        _window.ApplyGlass(handle);
        ApplyCaptureSetting();

        // Прогрев: один раз показываем окно за краем экрана без активации — дальше оно открывается мгновенно
        _window.ShowActivated = false;
        _window.Left = -32000;
        _window.Top = -32000;
        _window.Show();
        _window.Hide();
        _window.ShowActivated = true;

        // Невидимое окно только для сообщений о нажатии клавиши
        _messages = new HwndSource(new HwndSourceParameters("AncoraOverlayHotkey") { ParentWindow = HWND_MESSAGE });
        _messages.AddHook(WndProc);

        _hookProc = OnForegroundChanged;
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        OnForeground(Native.GetForegroundWindow());

        // Подстраховка: Windows иногда не присылает событие о смене окна (например, когда игра
        // пересоздаёт окно при загрузке или переключении режима экрана). Раз в секунду сверяемся сами.
        _foregroundCheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _foregroundCheck.Tick += (_, _) =>
        {
            var current = Native.GetForegroundWindow();
            if (current != _lastForeground) OnForeground(current);
        };
        _foregroundCheck.Start();
    }

    private DispatcherTimer? _foregroundCheck;
    private IntPtr _lastForeground;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Toggle();
        }
        return IntPtr.Zero;
    }

    private void OnForegroundChanged(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        OnForeground(hwnd);

    private void OnForeground(IntPtr hwnd)
    {
        try
        {
            _lastForeground = hwnd;
            var overlay = _window is null ? IntPtr.Zero : new WindowInteropHelper(_window).Handle;
            if (hwnd == overlay && hwnd != IntPtr.Zero) return;

            if (IsGameWindow(hwnd))
            {
                _gameWindow = hwnd;
                UpdateHotkey();
            }
            else
            {
                Unregister();
                // Игрок переключился в другую программу — оверлей не должен висеть поверх неё
                if (_window?.IsVisible == true) HideOverlay(returnFocus: false);
            }
        }
        catch (Exception ex)
        {
            Log("Ошибка при смене окна", ex);
        }
    }

    private bool IsGameWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        if (_gameProcesses.TryGetValue(pid, out var known)) return known;
        bool isGame;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            isGame = GameOptions.GameProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            isGame = false;
        }
        if (_gameProcesses.Count > 200) _gameProcesses.Clear();
        _gameProcesses[pid] = isGame;
        return isGame;
    }

    private void UpdateHotkey()
    {
        var gameInFront = _gameWindow != IntPtr.Zero && Native.GetForegroundWindow() is var fg
                          && (fg == _gameWindow || (_window is not null && fg == new WindowInteropHelper(_window).Handle));
        var wanted = Hotkey.Parse(_settings.Hotkey);
        if (_registered && wanted != _hotkey) Unregister();
        _hotkey = wanted;
        if (!_settings.Enabled || !gameInFront)
        {
            Unregister();
            return;
        }
        if (_registered || _messages is null) return;

        _registered = Native.RegisterHotKey(_messages.Handle, HotkeyId, _hotkey.NativeModifiers, _hotkey.VirtualKey);
        SetProblem(_registered ? null : L.F("Клавиша {0} занята другой программой — выбери другую в настройках", _hotkey));
    }

    private void Unregister()
    {
        if (!_registered || _messages is null) return;
        Native.UnregisterHotKey(_messages.Handle, HotkeyId);
        _registered = false;
    }

    private void SetProblem(string? problem)
    {
        if (problem == HotkeyProblem) return;
        HotkeyProblem = problem;
        if (problem is not null) Log(problem, null);
        HotkeyProblemChanged?.Invoke(problem);
    }

    private void ApplyCaptureSetting()
    {
        if (_window is null) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;
        Native.SetWindowDisplayAffinity(handle, _settings.HideFromCapture ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
    }

    private void Toggle()
    {
        if (_window is null || _viewModel is null) return;
        if (_window.IsVisible) HideOverlay(returnFocus: true);
        else ShowOverlay();
    }

    private void ShowOverlay()
    {
        if (_window is null || _viewModel is null) return;
        if (_gameWindow == IntPtr.Zero || !Native.IsWindow(_gameWindow) || Native.IsIconic(_gameWindow)) return;

        _window.Show();
        DockToGame();
        _window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(_window).Handle);
        _window.PlayAppear();
        _viewModel.Opened();
    }

    private void HideOverlay(bool returnFocus)
    {
        if (_window is null || !_window.IsVisible) return;
        _window.Hide();
        _viewModel?.Closed();
        if (returnFocus && _gameWindow != IntPtr.Zero && Native.IsWindow(_gameWindow))
            Native.SetForegroundWindow(_gameWindow);
    }

    /// <summary>
    /// Панель прижимается к левому краю окна игры с небольшим отступом и тянется по высоте —
    /// не закрывает середину экрана, где происходит игра.
    /// </summary>
    private void DockToGame()
    {
        var handle = new WindowInteropHelper(_window!).Handle;
        if (!Native.GetWindowRect(_gameWindow, out var game)) return;
        var scale = Native.GetDpiForWindow(_gameWindow) / 96.0;
        if (scale <= 0) scale = 1;
        var margin = (int)Math.Round(16 * scale);
        var width = (int)Math.Round(OverlayWindow.PanelWidth * scale);
        var height = Math.Max((int)Math.Round(360 * scale), game.Height - margin * 2);
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, game.Left + margin, game.Top + margin, width, height, 0);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ошибка в оверлее не должна ронять ни программу, ни игру: записываем и прячем окно
        e.Handled = true;
        Log("Ошибка оверлея", e.Exception);
        try { HideOverlay(returnFocus: true); }
        catch (Exception) { }
    }

    private void Log(string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            var info = new FileInfo(_logPath);
            if (info.Exists && info.Length > 512 * 1024) info.Delete();
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(ex is null ? "" : "\n" + ex)}\n");
        }
        catch (Exception) { }
    }
}
