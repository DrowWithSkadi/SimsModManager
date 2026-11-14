using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Helper;
using SimsModManager.Core.Saves;
using SimsModManager.Core;

namespace SimsModManager.App.Overlay;

public enum OverlayTab { Checkpoints, Commands }

public sealed partial class CheckpointItem(Checkpoint checkpoint) : ObservableObject
{
    public Checkpoint Checkpoint { get; } = checkpoint;
    public string Name => Checkpoint.Name;

    public string Details
    {
        get
        {
            var when = Checkpoint.CreatedAt.Date == DateTime.Today ? L.F("сегодня, {0:HH:mm}", Checkpoint.CreatedAt)
                : Checkpoint.CreatedAt.Date == DateTime.Today.AddDays(-1) ? L.F("вчера, {0:HH:mm}", Checkpoint.CreatedAt)
                : Checkpoint.CreatedAt.ToString("d MMMM, HH:mm");
            var size = L.F("{0:0.#} МБ", Checkpoint.SizeBytes / 1024.0 / 1024.0);
            return Checkpoint.GameSaveName is { Length: > 0 } game ? $"{when} · {game} · {size}" : $"{when} · {size}";
        }
    }

    public string KindLabel => Checkpoint.Kind switch
    {
        CheckpointKind.Auto => L.T("авто"),
        CheckpointKind.BeforeRollback => L.T("до отката"),
        _ => "",
    };

    public bool HasKindLabel => KindLabel.Length > 0;

    [ObservableProperty] private bool _isPinned = checkpoint.Pinned;
    [ObservableProperty] private bool _confirmRollback;
    [ObservableProperty] private bool _confirmDelete;
}

/// <summary>Содержимое оверлея. Живёт в потоке оверлея — с главным окном не пересекается.</summary>
public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly Func<CheckpointManager?> _manager;
    private readonly Func<bool> _pausesGame;
    private readonly Action _close;
    private readonly DispatcherTimer _statusTimer;
    private bool _pausedByUs;

    public OverlayViewModel(Func<CheckpointManager?> manager, Func<bool> pausesGame, Action close)
    {
        _manager = manager;
        _pausesGame = pausesGame;
        _close = close;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
    }

    public ObservableCollection<CheckpointItem> Checkpoints { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCheckpointsTab), nameof(IsCommandsTab))]
    private OverlayTab _tab;

    public bool IsCheckpointsTab => Tab == OverlayTab.Checkpoints;
    public bool IsCommandsTab => Tab == OverlayTab.Commands;

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MakeCheckpointCommand))] private bool _isBusy;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusOk;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _storageText = "";

    public void Opened()
    {
        RefreshStatus();
        RefreshList();
        _statusTimer.Start();
        if (_pausesGame() && !_pausedByUs && _manager()?.Bridge.ReadStatus().State == HelperState.Live)
        {
            _pausedByUs = true;
            _ = _manager()!.Bridge.SendAsync("pause", TimeSpan.FromSeconds(2));
        }
    }

    public void Closed()
    {
        _statusTimer.Stop();
        _closeAfterMenu = false;
        _sawMenu = false;
        foreach (var item in Checkpoints)
        {
            item.ConfirmRollback = false;
            item.ConfirmDelete = false;
        }
        if (_pausedByUs)
        {
            _pausedByUs = false;
            _ = _manager()?.Bridge.SendAsync("resume", TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>После отката панель показывает, как загрузить точку; как только игра начала грузиться — прячем её.</summary>
    private bool _closeAfterMenu;
    private bool _sawMenu;

    private void CloseWhenGameResumes(HelperState state)
    {
        if (!_closeAfterMenu) return;
        if (state == HelperState.Menu)
        {
            _sawMenu = true;
        }
        else if (_sawMenu)
        {
            _closeAfterMenu = false;
            _close();
        }
    }

    private void RefreshStatus()
    {
        var manager = _manager();
        if (manager is null)
        {
            (StatusText, StatusOk) = (L.T("Папка игры не найдена"), false);
            return;
        }
        var status = manager.Bridge.ReadStatus();
        CloseWhenGameResumes(status.State);
        (StatusText, StatusOk) = status.State switch
        {
            HelperState.Live => (status.SlotName is { Length: > 0 } n ? L.F("В игре · {0}", n) : L.T("В игре"), true),
            HelperState.Menu => (L.T("Главное меню"), true),
            HelperState.Starting => (L.T("Игра загружается"), true),
            _ => (L.T("Помощник не отвечает"), false),
        };
    }

    private void RefreshList()
    {
        var manager = _manager();
        Checkpoints.Clear();
        if (manager is null) { IsEmpty = true; return; }
        var all = manager.Store.List();
        foreach (var c in all) Checkpoints.Add(new CheckpointItem(c));
        IsEmpty = Checkpoints.Count == 0;
        var total = all.Sum(c => c.SizeBytes) / 1024.0 / 1024.0;
        StorageText = all.Count == 0 ? "" : L.F("{0} · {1:0.#} МБ", L.P(all.Count, "точка|точки|точек"), total);
    }


    private void Say(string? text, bool error = false) => (Message, MessageIsError) = (text, error);

    [RelayCommand] private void ShowCheckpoints() => Tab = OverlayTab.Checkpoints;
    [RelayCommand] private void ShowCommands() => Tab = OverlayTab.Commands;
    [RelayCommand] private void Close() => _close();

    private bool CanMake() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanMake))]
    private async Task MakeCheckpoint()
    {
        var manager = _manager();
        if (manager is null) return;
        IsBusy = true;
        BusyText = manager.Bridge.ReadStatus().State == HelperState.Live ? L.T("Игра сохраняется…") : L.T("Копирую сохранение…");
        Say(null);
        try
        {
            var checkpoint = await manager.MakeAsync(NewName);
            NewName = "";
            RefreshList();
            Say(L.F("Точка «{0}» готова", checkpoint.Name));
        }
        catch (Exception ex) when (ex is CheckpointException or InvalidSaveException or IOException or UnauthorizedAccessException)
        {
            Say(ex.Message, error: true);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    [RelayCommand]
    private void TogglePin(CheckpointItem item)
    {
        var manager = _manager();
        if (manager is null) return;
        item.IsPinned = !item.IsPinned;
        item.Checkpoint.Pinned = item.IsPinned;
        try { manager.Store.Update(item.Checkpoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say(ex.Message, true); }
    }

    [RelayCommand]
    private void AskRollback(CheckpointItem item)
    {
        foreach (var other in Checkpoints) { other.ConfirmRollback = false; other.ConfirmDelete = false; }
        item.ConfirmRollback = true;
    }

    [RelayCommand]
    private void AskDelete(CheckpointItem item)
    {
        foreach (var other in Checkpoints) { other.ConfirmRollback = false; other.ConfirmDelete = false; }
        item.ConfirmDelete = true;
    }

    [RelayCommand]
    private void Cancel(CheckpointItem item)
    {
        item.ConfirmRollback = false;
        item.ConfirmDelete = false;
    }

    [RelayCommand]
    private void Delete(CheckpointItem item)
    {
        var manager = _manager();
        if (manager is null) return;
        try
        {
            manager.Store.Delete(item.Checkpoint);
            RefreshList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say(ex.Message, true); }
    }

    [RelayCommand]
    private async Task Rollback(CheckpointItem item)
    {
        var manager = _manager();
        if (manager is null || IsBusy) return;
        item.ConfirmRollback = false;
        IsBusy = true;
        Say(null);
        var progress = new Progress<RollbackStep>(step => BusyText = step switch
        {
            RollbackStep.SavingCurrent => L.T("Сохраняю текущую игру, чтобы к ней можно было вернуться…"),
            RollbackStep.WaitingForMenu => L.T("Теперь выйди в главное меню: Esc → «Выйти» → «Выйти в главное меню». Сохранять не нужно — копия уже сделана."),
            RollbackStep.Restoring => L.T("Возвращаю сохранение…"),
            RollbackStep.LeavingToMenu => L.T("Игра выходит в главное меню…"),
            RollbackStep.WaitingForConfirm => L.T("Подтверди откат в окне игры."),
            RollbackStep.ClosingGame => L.T("Закрываю игру — открытое сохранение подменить нельзя…"),
            RollbackStep.Launching => L.T("Запускаю игру. В главном меню нажми «Продолжить» — откроется точка."),
            _ => null,
        });
        try
        {
            // Пока ждём главное меню, игра не должна стоять на нашей паузе
            if (_pausedByUs)
            {
                _pausedByUs = false;
                await manager.Bridge.SendAsync("resume", TimeSpan.FromSeconds(2));
            }
            // Игра читает сохранение с диска только при запуске или через «Загрузить игру»
            // («Продолжить» в той же сессии берёт игру из памяти), поэтому из игры откатываем перезапуском.
            if (manager.Bridge.ReadStatus().State == HelperState.Live)
            {
                // Игра открыта: сохраняем, закрываем, подменяем, запускаем снова
                await manager.RestartRollbackAsync(item.Checkpoint, progress);
                RefreshList();
                Say(L.F("Игра запускается. В главном меню нажми «Продолжить» — откроется «{0}». Прежняя игра сохранена точкой «Перед откатом».", item.Name));
            }
            else
            {
                await manager.RollbackAsync(item.Checkpoint, progress);
                RefreshList();
                Say(L.F("Готово! В главном меню нажми «Загрузить игру», затем «Играть» у своего сохранения и выбери семью — откроется «{0}». Кнопка «Продолжить» не подойдёт: она вернёт игру без отката. Прежняя игра сохранена точкой «Перед откатом».", item.Name));
            }
        }
        catch (RollbackCancelledException ex)
        {
            RefreshList();
            Say(ex.Message);
        }
        catch (Exception ex) when (ex is CheckpointException or InvalidSaveException or IOException or UnauthorizedAccessException)
        {
            Say(ex.Message, error: true);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }
}
