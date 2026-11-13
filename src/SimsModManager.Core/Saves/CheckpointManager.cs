using SimsModManager.Core.Helper;

namespace SimsModManager.Core.Saves;

public class CheckpointException(string message) : Exception(message);

/// <summary>Игрок нажал «Остаться» в окне игры.</summary>
public sealed class RollbackCancelledException() : CheckpointException(L.T("Откат отменён — игра продолжается"));

public enum RollbackStep
{
    /// <summary>Сохраняем текущую игру, чтобы к ней можно было вернуться.</summary>
    SavingCurrent,
    /// <summary>Ждём, пока игрок выйдет в главное меню (или закроет игру).</summary>
    WaitingForMenu,
    /// <summary>Игра сама выходит в главное меню — от игрока ничего не нужно.</summary>
    LeavingToMenu,
    /// <summary>В игре показано окно «Вернуться к точке?» — ждём ответа игрока.</summary>
    WaitingForConfirm,
    /// <summary>Кладём точку на место сохранения.</summary>
    Restoring,
    /// <summary>Закрываем игру — подменить открытое сохранение нельзя.</summary>
    ClosingGame,
    /// <summary>Запускаем игру снова.</summary>
    Launching,
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
            if (!response.Ok) throw new CheckpointException(L.F("Игра не сохранилась: {0}", response.Error));
            var slotId = (long?)response.Result?["slot_id"] ?? status.SlotId
                ?? throw new CheckpointException(L.T("Игра не сказала, какое сохранение открыто"));
            gameName = (string?)response.Result?["slot_name"] ?? gameName;
            slotPath = Path.Combine(paths.Saves, SaveSlots.FileName(slotId));
        }
        else
        {
            if (_isGameRunning() && status.State != HelperState.Menu)
                throw new CheckpointException(L.T("Игра открыта, но помощник не отвечает — сохрани игру сама и попробуй ещё раз, или проверь, что скриптовые моды включены"));
            slotPath = SaveSlots.MostRecent(paths.Saves) ?? throw new CheckpointException(L.T("Сохранений пока нет"));
        }

        if (!File.Exists(slotPath)) throw new CheckpointException(L.T("Файл сохранения не найден"));
        var checkpoint = await Task.Run(() => store.Create(slotPath, name, kind, gameName,
            skipIfUnchanged: kind != CheckpointKind.Manual), cancellation);
        await Task.Run(() => store.ApplyRetention(Retention), cancellation);
        return checkpoint;
    }

    /// <summary>Помощник умеет выводить игру в главное меню (версия 6+ — через окно игры, 7+ — сам).</summary>
    public bool CanRollbackViaMenu
    {
        get
        {
            var status = bridge.ReadStatus();
            return status.State == HelperState.Live && status.Version >= 6;
        }
    }

    /// <summary>
    /// Откат без перезапуска игры: текущая игра сохраняется точкой «Перед откатом», игра выходит
    /// в главное меню без сохранения, и файл сохранения возвращается к точке. Остаётся нажать «Продолжить».
    /// Помощник 7+ отправляет игру в меню сам; если игра не послушалась — показываем окно с кнопкой (как в версии 6).
    /// </summary>
    public async Task MenuRollbackAsync(Checkpoint checkpoint, IProgress<RollbackStep>? progress = null, CancellationToken cancellation = default)
    {
        if (!CanRollbackViaMenu)
            throw new CheckpointException(L.T("Помощник не отвечает — нельзя безопасно сохранить игру перед откатом"));
        var canLeaveByItself = bridge.ReadStatus().Version >= 7;

        progress?.Report(RollbackStep.SavingCurrent);
        await MakeAsync("", CheckpointKind.BeforeRollback, cancellation);

        if (!canLeaveByItself || !await LeaveToMenuAsync(progress, cancellation))
            await LeaveToMenuViaDialogAsync(checkpoint, progress, cancellation);

        progress?.Report(RollbackStep.Restoring);
        await Task.Run(() => store.Restore(checkpoint, paths.Saves), cancellation);
        progress?.Report(RollbackStep.Done);
    }

    /// <summary>Сколько ждём, что игра начнёт выходить в меню, прежде чем показать окно с кнопкой.</summary>
    public TimeSpan LeaveStartTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Просим игру выйти в главное меню без окна. true — игра в меню (или закрыта), сохранение можно подменять;
    /// false — игра не отреагировала и осталась в участке.
    /// </summary>
    private async Task<bool> LeaveToMenuAsync(IProgress<RollbackStep>? progress, CancellationToken cancellation)
    {
        var response = await bridge.SendAsync("to_menu", TimeSpan.FromSeconds(5), cancellation: cancellation);
        if (!response.Ok) return false;

        progress?.Report(RollbackStep.LeavingToMenu);
        var started = DateTime.UtcNow;
        // Всё ещё в участке спустя время — выход так и не начался
        return await WaitForMenuAsync(s => s.State == HelperState.Live && DateTime.UtcNow - started > LeaveStartTimeout, cancellation);
    }

    /// <summary>Сколько помощник может молчать при живой игре (например, на долгом экране загрузки), прежде чем откат остановится.</summary>
    public TimeSpan HelperSilenceTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Ждёт, пока сохранение освободится: игра вышла в главное меню или закрыта. Возвращает false,
    /// если <paramref name="giveUp"/> решил больше не ждать. «Помощник молчит» само по себе ничего не значит:
    /// на экране загрузки он может молчать несколько секунд — поэтому закрытой игру считаем, только когда нет её процесса.
    /// Иначе точка легла бы на место сохранения, пока игра ещё открыта.
    /// </summary>
    private async Task<bool> WaitForMenuAsync(Func<HelperStatus, bool> giveUp, CancellationToken cancellation)
    {
        DateTime? silentSince = null;
        while (true)
        {
            var status = bridge.ReadStatus();
            if (status.State == HelperState.Menu) return true;
            if (status.State == HelperState.Offline)
            {
                if (!_isGameRunning()) return true;
                silentSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - silentSince > HelperSilenceTimeout)
                    throw new CheckpointException(L.T("Игра перестала отвечать — откат остановлен, сохранение не тронуто. Текущая игра есть в точке «Перед откатом»."));
            }
            else
            {
                silentSince = null;
                if (giveUp(status)) return false;
            }
            await Task.Delay(200, cancellation);
        }
    }

    private async Task LeaveToMenuViaDialogAsync(Checkpoint checkpoint, IProgress<RollbackStep>? progress, CancellationToken cancellation)
    {
        var response = await bridge.SendAsync("menu_dialog", TimeSpan.FromSeconds(10), new Dictionary<string, object?>
        {
            ["title"] = "Sims Mod Manager",
            ["text"] = L.F("Вернуться к точке «{0}»?\n\nТекущая игра уже сохранена отдельной точкой «Перед откатом» — к ней тоже можно вернуться.\n\nИгра выйдет в главное меню. Там нажми «Продолжить».", checkpoint.Name),
            ["ok"] = L.T("Вернуться к точке"),
            ["cancel"] = L.T("Остаться"),
        }, cancellation);
        if (!response.Ok)
        {
            // Помощник молчит, потому что игра уже выгружает участок (выход в меню всё-таки начался) —
            // тогда окно не нужно, просто дожидаемся меню
            if (bridge.ReadStatus().State == HelperState.Live)
                throw new CheckpointException(L.F("Игра не показала окно: {0}", response.Error));
            progress?.Report(RollbackStep.LeavingToMenu);
        }
        else
        {
            progress?.Report(RollbackStep.WaitingForConfirm);
        }

        if (!await WaitForMenuAsync(s => s.Dialog == "cancelled" && s.State == HelperState.Live, cancellation))
            throw new RollbackCancelledException();
    }

    /// <summary>Запуск игры (задаёт программа: Steam, EA app или exe). true — запуск начат.</summary>
    public Func<bool>? LaunchGame { get; set; }

    /// <summary>Закрыть игру. По умолчанию — завершить её процесс (вызывается только после сохранения).</summary>
    public Func<TimeSpan, Task<bool>> CloseGame { get; set; } = CloseGameProcessAsync;

    /// <summary>
    /// Откат из игры одной кнопкой: текущая игра сохраняется точкой «Перед откатом», игра закрывается,
    /// файл сохранения возвращается к точке и игра запускается снова. В главном меню остаётся нажать
    /// «Продолжить» — точка будет самым свежим сохранением.
    /// </summary>
    public async Task RestartRollbackAsync(Checkpoint checkpoint, IProgress<RollbackStep>? progress = null, CancellationToken cancellation = default)
    {
        var status = bridge.ReadStatus();
        if (status.State != HelperState.Live)
            throw new CheckpointException(L.T("Помощник не отвечает — нельзя безопасно сохранить игру перед откатом"));

        progress?.Report(RollbackStep.SavingCurrent);
        await MakeAsync("", CheckpointKind.BeforeRollback, cancellation);

        // Игра уже сохранена и копия у нас — закрывать безопасно
        progress?.Report(RollbackStep.ClosingGame);
        if (!await CloseGame(TimeSpan.FromSeconds(20)))
            throw new CheckpointException(L.T("Не получилось закрыть игру. Закрой её сама — и нажми «Откатить» ещё раз"));

        progress?.Report(RollbackStep.Restoring);
        await Task.Run(() => store.Restore(checkpoint, paths.Saves), cancellation);

        progress?.Report(RollbackStep.Launching);
        if (LaunchGame is null || !LaunchGame())
            throw new CheckpointException(L.T("Сохранение возвращено к точке, но игру не удалось запустить — запусти её сама и нажми «Продолжить»"));
        progress?.Report(RollbackStep.Done);
    }

    private static async Task<bool> CloseGameProcessAsync(TimeSpan timeout)
    {
        var processes = GameOptions.GameProcessNames.SelectMany(System.Diagnostics.Process.GetProcessesByName).ToList();
        try
        {
            foreach (var p in processes)
            {
                try { p.Kill(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (processes.All(p => { try { return p.HasExited; } catch (InvalidOperationException) { return true; } }))
                {
                    await Task.Delay(1000); // даём системе отпустить файлы игры
                    return true;
                }
                await Task.Delay(200);
            }
            return false;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
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
            await WaitForMenuAsync(_ => false, cancellation);
        }
        else
        {
            if (_isGameRunning() && status.State != HelperState.Menu)
                throw new CheckpointException(L.T("Игра открыта, но помощник не отвечает — выйди из игры и повтори откат"));
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
