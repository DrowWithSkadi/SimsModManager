using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;
using SimsModManager.Core.Health;

namespace SimsModManager.App.ViewModels;

/// <summary>Проверка модов: конфликты, ошибки игры, обновление игры и «Найти виновника».</summary>
public sealed partial class MainViewModel
{
    // ---------- Проверка ----------

    public ObservableCollection<HealthIssueViewModel> HealthIssues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthBadgeText), nameof(HasHealthBadge), nameof(HasHealthBanner), nameof(HealthBannerText), nameof(HasDuplicates))]
    private int _healthProblemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthStatusText), nameof(ShowHealthAllGood))]
    private bool _isCheckingHealth;

    [ObservableProperty] private double _healthProgress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthStatusText), nameof(ShowHealthAllGood))]
    private DateTime? _healthCheckedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorReports))]
    private int _errorReportCount;

    [ObservableProperty] private int _healthCheckedMods;

    public bool IsHealthPage => Page == AppPage.Health;
    public bool HasHealthBadge => HealthProblemCount > 0;
    public string HealthBadgeText => HealthProblemCount > 99 ? "99+" : HealthProblemCount.ToString();
    public bool HasErrorReports => ErrorReportCount > 0;

    /// <summary>Плашка в библиотеке: «Найдено 3 проблемы с модами».</summary>
    public bool HasHealthBanner => HealthProblemCount > 0 && !HealthBannerHidden;
    public string HealthBannerText => "С модами " + (HealthProblemCount == 1
        ? "есть проблема, которая может мешать игре"
        : "есть " + Text.Plural(HealthProblemCount, "проблема", "проблемы", "проблем") + ", которые могут мешать игре");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHealthBanner), nameof(HasDuplicates))]
    private bool _healthBannerHidden;

    public bool ShowHealthAllGood => !IsCheckingHealth && HealthCheckedAt is not null && HealthIssues.Count == 0;

    public string HealthStatusText => IsCheckingHealth
        ? "Смотрю моды…"
        : HealthCheckedAt is { } at
            ? $"{Text.Plural(HealthCheckedMods, "включённый мод", "включённых мода", "включённых модов")} · обновлено в {at:HH:mm}"
            : "";

    private CancellationTokenSource? _healthCts;

    [RelayCommand]
    private void OpenHealth()
    {
        Page = AppPage.Health;
        if (HealthCheckedAt is null && !IsCheckingHealth) _ = RunHealthCheck();
    }

    /// <summary>Проверка идёт в фоне: после запуска, установки и по кнопке «Проверить снова».</summary>
    [RelayCommand]
    private Task RunHealthCheck() => RunHealthCheckAsync(null);

    /// <param name="newModNames">Только что установленные моды — если с ними проблема, сразу предупреждаем.</param>
    private async Task RunHealthCheckAsync(ICollection<string>? newModNames)
    {
        if (_library is null) return;
        _healthCts?.Cancel();
        var cts = _healthCts = new CancellationTokenSource();
        var checker = new ModHealthChecker(_library.Paths);
        var mods = Mods.Select(m => m.Model).ToList();
        var progress = new Progress<double>(p => HealthProgress = p);

        IsCheckingHealth = true;
        HealthProgress = 0;
        try
        {
            var report = await Task.Run(() => checker.Check(mods, progress, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            ApplyHealthReport(report);
            if (newModNames is { Count: > 0 }) await WarnAboutNewModsAsync(newModNames);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Не получилось посмотреть моды: " + ex.Message, Icons.Alert);
        }
        finally
        {
            if (_healthCts == cts) IsCheckingHealth = false;
        }
    }

    private void ApplyHealthReport(HealthReport report)
    {
        var ignored = new HashSet<string>(_settings.IgnoredIssues);
        HealthIssues.Clear();
        foreach (var issue in report.Issues.Where(i => !ignored.Contains(i.Key)))
            HealthIssues.Add(new HealthIssueViewModel(issue, this));
        HealthCheckedMods = report.CheckedMods;
        ErrorReportCount = report.ErrorReports.Count;
        HealthCheckedAt = DateTime.Now;
        UpdateHealthCount();
    }

    private void UpdateHealthCount()
    {
        HealthProblemCount = HealthIssues.Count(i => i.Issue.Severity != HealthSeverity.Info);
        OnPropertyChanged(nameof(CanFixAll));
        OnPropertyChanged(nameof(ShowHealthAllGood));
        UpdateModFlags();
    }

    /// <summary>Красный флажок у модов в списке: причины — в подсказке при наведении.</summary>
    private void UpdateModFlags()
    {
        var reasons = new Dictionary<ModItemViewModel, List<string>>();
        foreach (var issue in HealthIssues.Where(i => i.Issue.Severity != HealthSeverity.Info))
        foreach (var row in issue.Mods)
        {
            if (!reasons.TryGetValue(row.Mod, out var list)) reasons[row.Mod] = list = [];
            list.Add(issue.FlagReason(row));
        }
        foreach (var mod in Mods)
            mod.HealthFlag = reasons.TryGetValue(mod, out var list) ? string.Join(Environment.NewLine + Environment.NewLine, list) : null;
    }

    /// <summary>
    /// После установки: проверяем новые моды. Что можно исправить само — показываем планом и исправляем
    /// после одного «Исправить»; конфликты, где нужен выбор, — объясняем; про недостающие дополнения — предупреждаем.
    /// </summary>
    private async Task WarnAboutNewModsAsync(ICollection<string> newModNames)
    {
        var issues = HealthIssues
            .Where(i => i.Issue.Severity != HealthSeverity.Info && i.Mods.Any(r => newModNames.Contains(r.Name)))
            .ToList();
        var needPacks = Mods.Where(m => newModNames.Contains(m.Name) && m.MissingPack).ToList();
        if (issues.Count == 0 && needPacks.Count == 0) return;

        var packNote = needPacks.Count == 0 ? null
            : string.Join(Environment.NewLine, needPacks.Take(3).Select(m =>
                  $"«{m.Name}» сделан для дополнения «{GamePacks.NameOf(m.RequiredPacks[0])}», которого у вас нет — вещи могут быть невидимыми."));

        // 1. Есть что исправить автоматически — план + одно подтверждение
        var plan = BuildRepairPlan(issues, includeOutdated: false);
        if (!plan.IsEmpty)
        {
            if (packNote is not null) ShowToast(packNote, Icons.Alert, seconds: 8);
            await RepairAsync(issues, includeOutdated: false,
                title: issues.Count == 1 ? $"С новым модом проблема: {issues[0].Title.TrimEnd('.')}" : "С новыми модами есть проблемы — вот что я исправлю");
            return;
        }

        // 2. Автоматически не исправить (нужен выбор) или только недостающее дополнение
        var first = issues.FirstOrDefault();
        var newMods = issues.SelectMany(i => i.Mods).Select(r => r.Mod).Concat(needPacks)
            .Where(m => newModNames.Contains(m.Name)).Distinct().ToList();
        var single = newMods.Count == 1 ? newMods[0] : null;

        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = first is null ? "Новому моду нужно дополнение"
                : issues.Count == 1 ? first.Title : $"С новыми модами {Text.Plural(issues.Count, "проблема", "проблемы", "проблем")}",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource(first is null ? "Icon.Layers" : "Icon.Flag"),
            IconIsDanger = first is not null,
            Message = first is null ? packNote
                : issues.Count == 1 ? first.Explanation : string.Join(Environment.NewLine, issues.Take(4).Select(i => "• " + i.Title)),
            Message2 = first is null ? "Купите или установите дополнение — или выключите мод, чтобы в игре не было пустых вещей."
                : (issues.Count == 1 ? first.Advice : "Проблемные моды отмечены в списке красным флажком — наведите на него, чтобы увидеть причину.")
                  + (packNote is null ? "" : Environment.NewLine + packNote),
            PrimaryText = single is { IsEnabled: true } ? $"Выключить «{single.Name}»" : "Посмотреть",
            ExtraText = single is not null && first is not null ? "Подробнее" : null,
            SecondaryText = "Оставить как есть",
        });

        if (answer == DialogResult.Primary && single is { IsEnabled: true }) await IssueDisableMod(single);
        else if (answer is DialogResult.Primary or DialogResult.Extra) Page = AppPage.Health;
    }

    /// <summary>Мод выключили или удалили — связанные с ним проблемы больше не актуальны.</summary>
    internal void ResolveIssuesFor(ModItemViewModel mod)
    {
        foreach (var issue in HealthIssues.Where(i => i.Involves(mod)).ToList())
            HealthIssues.Remove(issue);
        UpdateHealthCount();
    }

    private void RemoveIssue(HealthIssueViewModel issue)
    {
        HealthIssues.Remove(issue);
        UpdateHealthCount();
    }

    internal ModItemViewModel? FindMod(ModItem model) =>
        Mods.FirstOrDefault(m => string.Equals(m.Model.Path, model.Path, StringComparison.OrdinalIgnoreCase))
        ?? Mods.FirstOrDefault(m => m.Name == model.Name && m.IsTray == model.IsTray);

    [RelayCommand]
    private async Task IssueDisableMod(ModItemViewModel? mod)
    {
        if (mod is null || !mod.IsEnabled) return;
        if (!await EnsureGameClosedAsync("выключить мод")) return;
        mod.IsEnabled = false; // сам переместит файлы и покажет уведомление
        if (!mod.IsEnabled) ResolveIssuesFor(mod);
    }

    [RelayCommand]
    private async Task IssueDeleteMod(ModItemViewModel? mod)
    {
        if (mod is null) return;
        var before = Mods.Contains(mod);
        await DeleteMod(mod);
        if (before && !Mods.Contains(mod)) ResolveIssuesFor(mod);
    }

    [RelayCommand]
    private async Task FixIssue(HealthIssueViewModel? issue)
    {
        if (issue is null || _library is null) return;
        switch (issue.Issue.Kind)
        {
            case HealthIssueKind.TooDeep:
                if (!await EnsureGameClosedAsync("переложить файлы")) return;
                try
                {
                    foreach (var mod in issue.Mods.Select(m => m.Mod)) mod.ReplaceModel(_library.FixTooDeep(mod.Model));
                    ShowToast("Файлы переложены — теперь игра их увидит", Icons.Check);
                    SetLastAction("исправлена установка мода");
                    RemoveIssue(issue);
                    await RefreshAsync();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await ShowErrorAsync("Не удалось переложить файлы", "Файлы мода заняты другой программой.", ex);
                }
                break;

            case HealthIssueKind.ArchiveInMods when issue.Issue.FilePath is { } archive:
                if (!File.Exists(archive))
                {
                    RemoveIssue(issue);
                    return;
                }
                // Копируем архив во временную папку, ставим как обычно и убираем исходник из Mods.
                var temp = Path.Combine(Path.GetTempPath(), "SimsModManager", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                var copy = Path.Combine(temp, Path.GetFileName(archive));
                try
                {
                    File.Move(archive, copy);
                    RemoveIssue(issue);
                    await InstallAsync([copy]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await ShowErrorAsync("Не удалось установить архив", "Архив занят другой программой.", ex);
                }
                break;

            case HealthIssueKind.UnknownErrors:
                await StartBisect();
                break;
        }
    }

    [RelayCommand]
    private void IgnoreIssue(HealthIssueViewModel? issue)
    {
        if (issue is null) return;
        if (!_settings.IgnoredIssues.Contains(issue.Issue.Key)) _settings.IgnoredIssues.Add(issue.Issue.Key);
        SaveSettings();
        RemoveIssue(issue);
        ShowToast("Больше не покажу это предупреждение", Icons.Check, undo: () =>
        {
            _settings.IgnoredIssues.Remove(issue.Issue.Key);
            SaveSettings();
            HealthIssues.Add(issue);
            UpdateHealthCount();
        });
    }

    [RelayCommand]
    private void ResetIgnoredIssues()
    {
        _settings.IgnoredIssues.Clear();
        SaveSettings();
        _ = RunHealthCheck();
    }

    /// <summary>Удалить отчёты lastException — чтобы следующая ошибка была видна «с чистого листа».</summary>
    [RelayCommand]
    private void ClearErrorReports()
    {
        if (_library is null) return;
        var checker = new ModHealthChecker(_library.Paths);
        var removed = 0;
        foreach (var file in checker.ErrorReportFiles())
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var issue in HealthIssues.Where(i => i.Issue.Kind is HealthIssueKind.CausesErrors or HealthIssueKind.UnknownErrors).ToList())
            HealthIssues.Remove(issue);
        ErrorReportCount = 0;
        UpdateHealthCount();
        ShowToast(removed > 0 ? $"Удалено {Text.Plural(removed, "отчёт", "отчёта", "отчётов")} об ошибках" : "Отчётов об ошибках нет", Icons.Check);
    }

    [RelayCommand]
    private void HideHealthBanner() => HealthBannerHidden = true;

    /// <summary>
    /// «Не напоминать»: текущие проблемы больше не показываются (и после перезапуска),
    /// а новые — покажутся. Вернуть можно на странице «Состояние» → «Показать скрытые предупреждения».
    /// </summary>
    [RelayCommand]
    private void DismissHealthIssues()
    {
        var dismissed = HealthIssues.Where(i => i.Issue.Severity != HealthSeverity.Info).ToList();
        if (dismissed.Count == 0) return;
        var added = new List<string>();
        foreach (var issue in dismissed)
        {
            if (_settings.IgnoredIssues.Contains(issue.Issue.Key)) continue;
            _settings.IgnoredIssues.Add(issue.Issue.Key);
            added.Add(issue.Issue.Key);
        }
        SaveSettings();
        foreach (var issue in dismissed) HealthIssues.Remove(issue);
        UpdateHealthCount();
        ShowToast(dismissed.Count == 1 ? "Больше не напомню об этом" : "Больше не напомню об этих проблемах", Icons.Check, undo: () =>
        {
            foreach (var key in added) _settings.IgnoredIssues.Remove(key);
            SaveSettings();
            foreach (var issue in dismissed) HealthIssues.Add(issue);
            UpdateHealthCount();
        });
    }

    // ---------- Игра обновилась ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameUpdateText))]
    private string? _updatedGameVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameUpdateText), nameof(HasGameUpdate))]
    private int _outdatedScriptCount;

    public bool HasGameUpdate => UpdatedGameVersion is not null && OutdatedScriptCount > 0;

    public string GameUpdateText =>
        $"Игра обновилась до версии {UpdatedGameVersion}. " +
        $"{Text.Plural(OutdatedScriptCount, "скриптовый мод", "скриптовых мода", "скриптовых модов")} могли перестать работать — " +
        "проверьте, нет ли у авторов новых версий.";

    private List<ModItemViewModel> _outdatedScripts = [];

    /// <summary>Сравниваем версию игры с той, при которой всё работало.</summary>
    private void CheckGameVersion()
    {
        if (_library is null) return;
        var version = GameVersion.Read(_library.Paths);
        if (version is null) return;

        if (_settings.AcknowledgedGameVersion is null)
        {
            // Первый запуск менеджера — считаем текущую версию нормой.
            _settings.AcknowledgedGameVersion = version;
            SaveSettings();
            return;
        }
        if (version == _settings.AcknowledgedGameVersion)
        {
            UpdatedGameVersion = null;
            return;
        }

        _settings.GameUpdateDetectedAt ??= DateTime.Now;
        SaveSettings();
        var detected = _settings.GameUpdateDetectedAt.Value;
        var outdated = GameVersion.ScriptModsOlderThan(Mods.Select(m => m.Model), detected);
        _outdatedScripts = outdated.Select(FindMod).OfType<ModItemViewModel>().ToList();
        UpdatedGameVersion = version;
        OutdatedScriptCount = _outdatedScripts.Count;
        if (OutdatedScriptCount == 0) AcknowledgeGameUpdate();
    }

    [RelayCommand]
    private void AcknowledgeGameUpdate()
    {
        if (UpdatedGameVersion is not null) _settings.AcknowledgedGameVersion = UpdatedGameVersion;
        _settings.GameUpdateDetectedAt = null;
        SaveSettings();
        UpdatedGameVersion = null;
        OutdatedScriptCount = 0;
    }

    [RelayCommand]
    private void ShowOutdatedScripts()
    {
        ResetFilters();
        TypeFilter = TypeFilter.Scripts;
        Page = AppPage.Library;
    }

    [RelayCommand]
    private async Task DisableOutdatedScripts()
    {
        if (_library is null || !await EnsureGameClosedAsync("выключить скрипты")) return;
        var done = 0;
        foreach (var mod in _outdatedScripts.Where(m => m.IsEnabled && Mods.Contains(m)))
        {
            try
            {
                mod.ReplaceModel(_library.SetEnabled(mod.Model, false));
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        UpdateSummary();
        ShowToast($"Выключено: {Text.Plural(done, "скрипт", "скрипта", "скриптов")}. Включите их, когда авторы выпустят обновления.", Icons.Check);
        SetLastAction("выключены скрипты после обновления игры");
        AcknowledgeGameUpdate();
    }

    // ---------- Найти виновника (50/50) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBisect), nameof(BisectFinished), nameof(BisectInProgress), nameof(BisectStepText),
        nameof(BisectHint), nameof(BisectCulprit))]
    private ModBisect? _bisect;

    public bool HasBisect => Bisect is not null;
    public bool BisectInProgress => Bisect is { IsFinished: false };
    public bool BisectFinished => Bisect is { IsFinished: true };
    public string? BisectCulprit => Bisect?.Culprit;

    public string BisectStepText => Bisect is null ? "" : Bisect.IsFinished
        ? "Виновник найден"
        : $"Проверка {Bisect.Step} · осталось примерно {Text.Plural(Bisect.StepsLeft, "запуск", "запуска", "запусков")}";

    public string BisectHint => Bisect is null ? "" : Bisect.IsFinished
        ? $"Похоже, проблему вызывает «{Bisect.Culprit}». Остальные моды можно вернуть как было."
        : $"Сейчас выключено {Text.Plural(Bisect.Suspects.Count - Bisect.TestingNow.Count, "подозрительный мод", "подозрительных мода", "подозрительных модов")}. " +
          "Нажмите «Играть и проверить», повторите то, что ломалось, и закройте игру — я сам посмотрю её отчёты об ошибках и сделаю следующий шаг. " +
          "Если проблема не пишется в отчёты (например, что-то выглядит не так), ответьте кнопками.";

    /// <summary>Если поиск прервали (закрыли программу), продолжаем с того же места.</summary>
    private void LoadBisect()
    {
        if (_library is null) return;
        Bisect = ModBisect.Load(_library.Paths);
        // Игру запускали для проверки, а менеджер в это время закрыли — разбираемся сейчас
        if (Bisect is { IsFinished: false, LaunchedAt: not null } && !GameOptions.IsGameRunning())
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _ = AutoAnswerBisectAsync(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void MarkBisectLaunch()
    {
        if (_library is null || Bisect is not { IsFinished: false }) return;
        Bisect.LaunchedAt = DateTime.Now;
        try { Bisect.Save(_library.Paths); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private bool _autoAnswering;

    /// <summary>
    /// Игра закрылась после запуска для проверки: если она записала новые ошибки — проблема осталась,
    /// и следующий шаг делается сам. Если ошибок нет — коротко спрашиваем (проблема могла быть не «в отчётах»).
    /// </summary>
    private async Task AutoAnswerBisectAsync()
    {
        if (_autoAnswering || _library is null || Bisect is not { IsFinished: false, LaunchedAt: { } launchedAt }) return;
        _autoAnswering = true;
        try
        {
            await Task.Delay(2500); // игра дописывает отчёты при выходе
            var fresh = new ModHealthChecker(_library.Paths).ErrorReportsSince(launchedAt);
            Bisect.LaunchedAt = null;
            try { Bisect.Save(_library.Paths); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            if (fresh.Count > 0)
            {
                ShowToast($"Игра записала {Text.Plural(fresh.Count, "ошибку", "ошибки", "ошибок")} — значит, виновник среди включённых модов. Делаю следующий шаг.", Icons.Info, seconds: 6);
                await BisectAnswer("yes");
            }
            else
            {
                var answer = await ShowDialogAsync(new DialogViewModel
                {
                    Title = "Игра закрылась без ошибок",
                    Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.ScanSearch"),
                    Message = "В этот раз игра не записала ни одной ошибки. Проблема, которую мы ищем, была?",
                    PrimaryText = "Нет, всё работало",
                    ExtraText = "Да, была",
                    SecondaryText = "Не успел проверить",
                });
                if (answer == DialogResult.Primary) await BisectAnswer("no");
                else if (answer == DialogResult.Extra) await BisectAnswer("yes");
            }
            if (Bisect is { IsFinished: true }) Page = AppPage.Health;
        }
        finally
        {
            _autoAnswering = false;
        }
    }

    [RelayCommand]
    private async Task StartBisect()
    {
        if (_library is null || Bisect is not null) return;
        var enabled = Mods.Where(m => m.IsEnabled && !m.IsTray).ToList();
        if (enabled.Count < 2)
        {
            ShowToast("Для поиска нужно хотя бы два включённых мода", Icons.Info);
            return;
        }
        // Ошибки в отчётах игры почти всегда из-за скриптов и модов, меняющих поведение (настройки игры),
        // а не из-за одежды, причёсок и мебели. Начинаем с них — так нужно в разы меньше запусков.
        var likely = enabled.Where(m => m.HasScripts || m.Category is Core.Content.ModCategory.Scripts
            or Core.Content.ModCategory.Other or Core.Content.ModCategory.Unknown).ToList();
        var useLikely = likely.Count >= 2 && likely.Count < enabled.Count;
        int Runs(int n) => (int)Math.Ceiling(Math.Log2(Math.Max(2, n)));

        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = "Найти мод, который мешает игре",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.ScanSearch"),
            Message = "Я выключу половину модов. Нажмите «Играть и проверить», повторите то, что ломалось, и закройте игру — " +
                      "я сам посмотрю отчёты об ошибках и сделаю следующий шаг. Круг каждый раз сужается вдвое.",
            Message2 = useLikely
                ? $"Обычно виноваты скрипты и моды, меняющие игру ({likely.Count} шт.) — их проверка займёт около {Text.Plural(Runs(likely.Count), "запуска", "запусков", "запусков")}. " +
                  $"Если проблема с одеждой или мебелью — ищите среди всех ({Text.Plural(Runs(enabled.Count), "запуск", "запуска", "запусков")}). В конце все моды вернутся как были."
                : $"Понадобится около {Text.Plural(Runs(enabled.Count), "запуска", "запусков", "запусков")}. В конце все моды вернутся как были.",
            PrimaryText = useLikely ? $"Самые вероятные ({likely.Count})" : "Начать",
            ExtraText = useLikely ? $"Среди всех ({enabled.Count})" : null,
            SecondaryText = "Отмена",
        });
        if (answer == DialogResult.Cancel || !await EnsureGameClosedAsync("начать поиск")) return;

        var suspects = useLikely && answer == DialogResult.Primary ? likely : enabled;
        Bisect = ModBisect.Start(enabled.Select(m => m.Name), suspects.Select(m => m.Name));
        await ApplyBisectAsync();
        Page = AppPage.Health;
    }

    [RelayCommand]
    private async Task BisectAnswer(string? problemStillThere)
    {
        if (Bisect is null || Bisect.IsFinished) return;
        if (!await EnsureGameClosedAsync("продолжить поиск")) return;
        Bisect.Answer(problemStillThere == "yes");
        OnPropertyChanged(nameof(Bisect));
        NotifyBisect();
        await ApplyBisectAsync();
    }

    private void NotifyBisect()
    {
        foreach (var name in new[] { nameof(HasBisect), nameof(BisectFinished), nameof(BisectInProgress), nameof(BisectStepText), nameof(BisectHint), nameof(BisectCulprit) })
            OnPropertyChanged(name);
    }

    /// <summary>Включаем и выключаем моды так, как нужно для текущей проверки.</summary>
    private async Task ApplyBisectAsync()
    {
        if (_library is null || Bisect is null) return;
        var library = _library;
        var want = Bisect.IsFinished
            ? new HashSet<string>(Bisect.OriginallyEnabled.Where(n => n != Bisect.Culprit), StringComparer.OrdinalIgnoreCase)
            : Bisect.ShouldBeEnabled();
        await SetEnabledByNamesAsync(library, want, onlyAmong: Bisect.OriginallyEnabled);
        try { Bisect.Save(library.Paths); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        UpdateSummary();
    }

    private async Task SetEnabledByNamesAsync(ModLibrary library, IReadOnlySet<string> want, IReadOnlyList<string> onlyAmong)
    {
        var scope = new HashSet<string>(onlyAmong, StringComparer.OrdinalIgnoreCase);
        var changes = Mods.Where(m => !m.IsTray && scope.Contains(m.Name) && m.IsEnabled != want.Contains(m.Name)).ToList();
        IsLoading = changes.Count > 50;
        var failed = 0;
        var results = await Task.Run(() => changes.Select(m =>
        {
            try { return library.SetEnabled(m.Model, want.Contains(m.Name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Interlocked.Increment(ref failed);
                return null;
            }
        }).ToList());
        for (var i = 0; i < changes.Count; i++)
            if (results[i] is { } updated) changes[i].ReplaceModel(updated);
        IsLoading = false;
        if (failed > 0) ShowToast($"Не получилось переключить {Text.Plural(failed, "мод", "мода", "модов")} — файлы заняты", Icons.Alert);
    }

    /// <summary>Закончить: вернуть все моды как было. culpritAction: "keep" — вернуть и виновника, "disable" — оставить его выключенным, "delete" — удалить.</summary>
    [RelayCommand]
    private async Task FinishBisect(string? culpritAction)
    {
        if (_library is null || Bisect is null) return;
        if (!await EnsureGameClosedAsync("вернуть моды")) return;
        var bisect = Bisect;
        var culprit = bisect.Culprit;
        var want = new HashSet<string>(bisect.OriginallyEnabled, StringComparer.OrdinalIgnoreCase);
        if (culpritAction is "disable" or "delete" && culprit is not null) want.Remove(culprit);

        await SetEnabledByNamesAsync(_library, want, bisect.OriginallyEnabled);
        ModBisect.Clear(_library.Paths);
        Bisect = null;
        UpdateSummary();

        if (culpritAction == "delete" && culprit is not null && Mods.FirstOrDefault(m => m.Name == culprit) is { } mod)
            await DeleteMod(mod);
        else
            ShowToast(culpritAction == "disable" && culprit is not null
                ? $"Готово: «{culprit}» выключен, остальные моды включены как раньше"
                : "Все моды вернулись как были", Icons.Check);
        SetLastAction(culpritAction == "disable" ? "найден и выключен проблемный мод" : "поиск виновника завершён");
    }
}

/// <summary>Одна проблема на экране «Проверка модов».</summary>
public sealed partial class HealthIssueViewModel : ObservableObject
{
    public HealthIssueViewModel(HealthIssue issue, MainViewModel owner)
    {
        Issue = issue;
        Mods = issue.Mods
            .Select(m => owner.FindMod(m) is { } vm ? new HealthModRow(vm, issue.Suggested is not null && issue.Suggested.Path == m.Path) : null)
            .OfType<HealthModRow>()
            .ToList();
    }

    public HealthIssue Issue { get; }
    public IReadOnlyList<HealthModRow> Mods { get; }

    public string Title => Issue.Title;
    public string Explanation => Issue.Explanation;
    public string Advice => Issue.Advice;
    public string? Details => Issue.Details;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Issue.Details);
    public bool HasMods => Mods.Count > 0;

    public bool IsProblem => Issue.Severity == HealthSeverity.Problem;
    public bool IsWarning => Issue.Severity == HealthSeverity.Warning;
    public bool IsInfo => Issue.Severity == HealthSeverity.Info;

    public string SeverityText => Issue.Severity switch
    {
        HealthSeverity.Problem => "Мешает игре",
        HealthSeverity.Warning => "Стоит проверить",
        _ => "Можно не трогать",
    };

    /// <summary>Главная кнопка для проблем, которые менеджер исправит сам.</summary>
    public string? FixText => Issue.Kind switch
    {
        HealthIssueKind.TooDeep => "Исправить",
        HealthIssueKind.ArchiveInMods => "Установить",
        HealthIssueKind.UnknownErrors => "Найти виновника",
        _ => null,
    };

    public bool HasFix => FixText is not null;
    public bool IsErrorReport => Issue.Kind is HealthIssueKind.CausesErrors or HealthIssueKind.UnknownErrors;

    [ObservableProperty] private bool _detailsExpanded;

    public bool Involves(ModItemViewModel mod) => Mods.Any(r => r.Mod == mod);

    /// <summary>Короткая причина для подсказки у флажка в списке.</summary>
    public string FlagReason(HealthModRow row)
    {
        var others = Mods.Where(r => r != row).Select(r => $"«{r.Name}»").ToList();
        var text = Issue.Kind switch
        {
            HealthIssueKind.Conflict => $"Мешает моду {string.Join(", ", others)}: оба меняют одно и то же, работать будет только один.",
            HealthIssueKind.ScriptConflict => $"Похоже на другую версию {string.Join(", ", others)} — две копии скрипта мешают друг другу.",
            HealthIssueKind.Duplicate => $"Установлен ещё раз как {string.Join(", ", others)}.",
            HealthIssueKind.TooDeep => "Файлы лежат слишком глубоко в папках — игра мод не видит.",
            HealthIssueKind.Broken => "Файл повреждён — скорее всего, недокачался.",
            HealthIssueKind.CausesErrors => "Из-за этого мода в игре были ошибки.",
            _ => Issue.Title,
        };
        if (row.IsSuggested) text += " Лучше убрать именно этот.";
        return text;
    }
}

public sealed record HealthModRow(ModItemViewModel Mod, bool IsSuggested)
{
    public string Name => Mod.Name;
    public string DateText => "установлен " + Mod.Model.ModifiedAt.ToString("dd.MM.yyyy");
}
