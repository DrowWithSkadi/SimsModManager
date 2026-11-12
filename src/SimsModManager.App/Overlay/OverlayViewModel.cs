using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Helper;
using SimsModManager.Core.Saves;

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
            var when = Checkpoint.CreatedAt.Date == DateTime.Today ? $"сегодня, {Checkpoint.CreatedAt:HH:mm}"
                : Checkpoint.CreatedAt.Date == DateTime.Today.AddDays(-1) ? $"вчера, {Checkpoint.CreatedAt:HH:mm}"
                : Checkpoint.CreatedAt.ToString("d MMMM, HH:mm");
            var size = $"{Checkpoint.SizeBytes / 1024.0 / 1024.0:0.#} МБ";
            return Checkpoint.GameSaveName is { Length: > 0 } game ? $"{when} · {game} · {size}" : $"{when} · {size}";
        }
    }

    public string KindLabel => Checkpoint.Kind switch
    {
        CheckpointKind.Auto => "авто",
        CheckpointKind.BeforeRollback => "до отката",
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

    private void RefreshStatus()
    {
        var manager = _manager();
        if (manager is null)
        {
            (StatusText, StatusOk) = ("Папка игры не найдена", false);
            return;
        }
        var status = manager.Bridge.ReadStatus();
        (StatusText, StatusOk) = status.State switch
        {
            HelperState.Live => (status.SlotName is { Length: > 0 } n ? $"В игре · {n}" : "В игре", true),
            HelperState.Menu => ("Главное меню", true),
            HelperState.Starting => ("Игра загружается", true),
            _ => ("Помощник не отвечает", false),
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
        StorageText = all.Count == 0 ? "" : $"{all.Count} {Plural(all.Count, "точка", "точки", "точек")} · {total:0.#} МБ";
    }

    private static string Plural(int n, string one, string few, string many) =>
        (n % 10, n % 100) switch
        {
            (1, not 11) => one,
            (>= 2 and <= 4, < 12 or > 14) => few,
            _ => many,
        };

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
        BusyText = manager.Bridge.ReadStatus().State == HelperState.Live ? "Игра сохраняется…" : "Копирую сохранение…";
        Say(null);
        try
        {
            var checkpoint = await manager.MakeAsync(NewName);
            NewName = "";
            RefreshList();
            Say($"Точка «{checkpoint.Name}» готова");
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
            RollbackStep.SavingCurrent => "Сохраняю текущую игру, чтобы к ней можно было вернуться…",
            RollbackStep.WaitingForMenu => "Теперь выйди в главное меню: Esc → «Выйти» → «Выйти в главное меню». Сохранять не нужно — копия уже сделана.",
            RollbackStep.Restoring => "Возвращаю сохранение…",
            RollbackStep.Loading => "Игра загружает точку…",
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
            if (manager.CanHotSwap)
            {
                // Без выхода из игры: как только пошла загрузка участка, панель убирается с экрана
                var hotProgress = new Progress<RollbackStep>(step =>
                {
                    ((IProgress<RollbackStep>)progress).Report(step);
                    if (step == RollbackStep.Loading) _close();
                });
                await manager.HotSwapAsync(item.Checkpoint, hotProgress);
                RefreshList();
                Say($"Готово — ты в точке «{item.Name}». Прежняя игра сохранена точкой «Перед откатом».");
            }
            else
            {
                await manager.RollbackAsync(item.Checkpoint, progress);
                RefreshList();
                Say($"Готово! Нажми «Продолжить» в главном меню — откроется «{item.Name}». Прежняя игра сохранена точкой «Перед откатом».");
            }
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
