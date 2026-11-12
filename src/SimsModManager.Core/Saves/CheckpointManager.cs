using SimsModManager.Core.Helper;

namespace SimsModManager.Core.Saves;

public sealed class CheckpointException(string message) : Exception(message);

public enum RollbackStep
{
    /// <summary>Сохраняем текущую игру, чтобы к ней можно было вернуться.</summary>
    SavingCurrent,
    /// <summary>Ждём, пока игрок выйдет в главное меню (или закроет игру).</summary>
    WaitingForMenu,
    /// <summary>Кладём точку на место сохранения.</summary>
    Restoring,
    /// <summary>Подмена на лету: игра загружает участок из точки.</summary>
    Loading,
    Done,
}

/// <summary>
/// Сценарии точек сохранения поверх хранилища и помощника: «сделать точку» и «откатиться».
/// Не зависит от интерфейса — им пользуются и оверлей, и окно программы.
/// </summary>
public sealed class CheckpointManager(SimsPaths paths, CheckpointStore store, HelperBridge bridge, Func<bool>? isGameRunning = null)
{
    public static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(90);

    private readonly Func<bool> _isGameRunning = isGameRunning ?? GameOptions.IsGameRunning;

    public CheckpointStore Store => store;
    public HelperBridge Bridge => bridge;

    /// <summary>Правила автоудаления — применяются после каждой новой точки.</summary>
    public RetentionRules Retention { get; set; } = new();

    /// <summary>
    /// Делает точку. Если игра открыта и помощник отвечает — сначала сохраняет игру, чтобы точка
    /// содержала именно текущий момент. Иначе копирует последнее сохранение игры.
    /// </summary>
    public async Task<Checkpoint> MakeAsync(string name, CheckpointKind kind = CheckpointKind.Manual, CancellationToken cancellation = default)
    {
        var status = bridge.ReadStatus();
        string slotPath;
        var gameName = status.SlotName;

        if (status.State == HelperState.Live)
        {
            var response = await bridge.SendAsync("save", SaveTimeout, cancellation: cancellation);
            if (!response.Ok) throw new CheckpointException($"Игра не сохранилась: {response.Error}");
            var slotId = (long?)response.Result?["slot_id"] ?? status.SlotId
                ?? throw new CheckpointException("Игра не сказала, какое сохранение открыто");
            gameName = (string?)response.Result?["slot_name"] ?? gameName;
            slotPath = Path.Combine(paths.Saves, SaveSlots.FileName(slotId));
        }
        else
        {
            if (_isGameRunning() && status.State != HelperState.Menu)
                throw new CheckpointException("Игра открыта, но помощник не отвечает — сохрани игру сама и попробуй ещё раз, или проверь, что скриптовые моды включены");
            slotPath = SaveSlots.MostRecent(paths.Saves) ?? throw new CheckpointException("Сохранений пока нет");
        }

        if (!File.Exists(slotPath)) throw new CheckpointException("Файл сохранения не найден");
        var checkpoint = await Task.Run(() => store.Create(slotPath, name, kind, gameName,
            skipIfUnchanged: kind != CheckpointKind.Manual), cancellation);
        await Task.Run(() => store.ApplyRetention(Retention), cancellation);
        return checkpoint;
    }

    /// <summary>Сколько ждать, пока игра загрузит участок после подмены на лету.</summary>
    public static readonly TimeSpan HotSwapTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Подмена на лету возможна: игра открыта и помощник умеет это делать.</summary>
    public bool CanHotSwap
    {
        get
        {
            var status = bridge.ReadStatus();
            return status.State == HelperState.Live && status.Version >= 2;
        }
    }

    /// <summary>
    /// Откат без выхода из игры (эксперимент). Текущая игра сохраняется точкой «Перед откатом»,
    /// файл сохранения возвращается к точке, а помощник подставляет данные точки в память игры
    /// и запускает перезагрузку участка — игрок видит обычный экран загрузки.
    /// </summary>
    public async Task HotSwapAsync(Checkpoint checkpoint, IProgress<RollbackStep>? progress = null, CancellationToken cancellation = default)
    {
        if (!CanHotSwap) throw new CheckpointException("Подмена на лету работает, только когда сохранение открыто в игре");

        progress?.Report(RollbackStep.SavingCurrent);
        await MakeAsync("", CheckpointKind.BeforeRollback, cancellation);

        progress?.Report(RollbackStep.Restoring);
        var files = new List<string>();
        await Task.Run(() =>
        {
            foreach (var old in Directory.Exists(bridge.Folder) ? Directory.GetFiles(bridge.Folder, "swap-*.bin") : [])
                File.Delete(old);
            Directory.CreateDirectory(bridge.Folder);
            var save = store.ReadData(checkpoint);
            var candidates = CheckpointStore.ExtractGameDataCandidates(save);
            for (var i = 0; i < candidates.Count; i++)
            {
                var file = Path.Combine(bridge.Folder, $"swap-{i}-{candidates[i].Type:X8}.bin");
                File.WriteAllBytes(file, candidates[i].Data);
                files.Add(file);
            }
            // Файл на диске тоже возвращаем: если игра вылетит, «Продолжить» откроет уже точку
            store.Restore(checkpoint, paths.Saves);
        }, cancellation);

        var response = await bridge.SendAsync("hotswap", TimeSpan.FromSeconds(15),
            new Dictionary<string, object?> { ["paths"] = files }, cancellation);
        if (!response.Ok) throw new CheckpointException($"Игра не приняла точку: {response.Error}");

        progress?.Report(RollbackStep.Loading);
        var deadline = DateTime.UtcNow + HotSwapTimeout;
        var sawLoading = false;
        while (DateTime.UtcNow < deadline)
        {
            var status = bridge.ReadStatus();
            if (status.Swap == "failed")
                throw new CheckpointException($"Игра не смогла подставить точку: {status.SwapError}");
            if (status.State != HelperState.Live) sawLoading = true;
            if (status.Swap == "applied" && status.State == HelperState.Live && sawLoading)
            {
                progress?.Report(RollbackStep.Done);
                return;
            }
            await Task.Delay(500, cancellation);
        }
        await bridge.SendAsync("hotswap_cancel", TimeSpan.FromSeconds(3));
        throw new CheckpointException("Игра так и не начала загрузку. Файл сохранения уже возвращён к точке — выйди в главное меню и нажми «Продолжить»");
    }

    /// <summary>
    /// Возвращает сохранение к точке. Текущее состояние всегда сохраняется отдельной точкой «Перед откатом».
    /// Пока сохранение открыто в игре, подменять файл нельзя — ждём выхода в главное меню.
    /// </summary>
    public async Task RollbackAsync(Checkpoint checkpoint, IProgress<RollbackStep>? progress = null, CancellationToken cancellation = default)
    {
        var status = bridge.ReadStatus();
        var current = Path.Combine(paths.Saves, checkpoint.SlotFile);

        if (status.State == HelperState.Live)
        {
            progress?.Report(RollbackStep.SavingCurrent);
            await MakeAsync("", CheckpointKind.BeforeRollback, cancellation);

            progress?.Report(RollbackStep.WaitingForMenu);
            while (bridge.ReadStatus().State is HelperState.Live or HelperState.Starting)
                await Task.Delay(300, cancellation);
        }
        else
        {
            if (_isGameRunning() && status.State != HelperState.Menu)
                throw new CheckpointException("Игра открыта, но помощник не отвечает — выйди из игры и повтори откат");
            if (File.Exists(current))
            {
                progress?.Report(RollbackStep.SavingCurrent);
                await Task.Run(() => store.Create(current, "", CheckpointKind.BeforeRollback, checkpoint.GameSaveName,
                    skipIfUnchanged: true), cancellation);
            }
        }

        progress?.Report(RollbackStep.Restoring);
        await Task.Run(() => store.Restore(checkpoint, paths.Saves), cancellation);
        progress?.Report(RollbackStep.Done);
    }
}
