using System.IO;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core.Health;

namespace SimsModManager.App.ViewModels;

/// <summary>
/// «Исправить всё»: менеджер сам решает, что делать с найденными проблемами, показывает план,
/// и после одного подтверждения всё делает. Моды не удаляются — только выключаются (вернуть можно одной кнопкой).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Что менеджер сделает с проблемами.</summary>
    private sealed class RepairPlan
    {
        public List<string> Lines { get; } = [];
        public HashSet<ModItemViewModel> Disable { get; } = [];
        public List<HealthIssueViewModel> TooDeep { get; } = [];
        public List<string> Archives { get; } = [];
        public bool OutdatedScripts { get; set; }
        /// <summary>Проблемы, которые сами по себе не исправить (покажем, что делать).</summary>
        public List<string> Manual { get; } = [];
        public List<HealthIssueViewModel> Resolved { get; } = [];
        public bool HasUnknownErrors { get; set; }

        public bool IsEmpty => Disable.Count == 0 && TooDeep.Count == 0 && Archives.Count == 0 && !OutdatedScripts;
    }

    /// <summary>Есть что исправить автоматически.</summary>
    public bool CanFixAll => !BuildRepairPlan(HealthIssues, includeOutdated: true).IsEmpty;

    private RepairPlan BuildRepairPlan(IEnumerable<HealthIssueViewModel> issues, bool includeOutdated)
    {
        var plan = new RepairPlan();

        void DisableMod(ModItemViewModel mod, string line)
        {
            if (!mod.IsEnabled || !plan.Disable.Add(mod)) return;
            plan.Lines.Add(line);
        }

        foreach (var issue in issues.Where(i => i.Issue.Severity != HealthSeverity.Info))
        {
            var rows = issue.Mods;
            switch (issue.Issue.Kind)
            {
                case HealthIssueKind.TooDeep:
                    plan.TooDeep.Add(issue);
                    plan.Lines.Add($"Переложу файлы {Names(rows)} повыше — сейчас игра их не видит");
                    plan.Resolved.Add(issue);
                    break;

                case HealthIssueKind.ArchiveInMods when issue.Issue.FilePath is { } archive && File.Exists(archive):
                    plan.Archives.Add(archive);
                    plan.Lines.Add($"Распакую и установлю архив «{Path.GetFileName(archive)}»");
                    plan.Resolved.Add(issue);
                    break;

                case HealthIssueKind.Duplicate or HealthIssueKind.ScriptConflict:
                {
                    // Оставляем более новую копию; если проверка сама подсказала, какую убрать, — её
                    var extra = rows.FirstOrDefault(r => r.IsSuggested)?.Mod
                                ?? rows.Where(r => r.Mod.IsEnabled).OrderBy(r => r.Mod.Model.ModifiedAt).Select(r => r.Mod).FirstOrDefault();
                    if (extra is not null && rows.Count(r => r.Mod.IsEnabled) >= 2)
                    {
                        DisableMod(extra, $"Выключу лишнюю копию «{extra.Name}» — оставлю более новую");
                        plan.Resolved.Add(issue);
                    }
                    break;
                }

                case HealthIssueKind.Broken:
                    foreach (var row in rows) DisableMod(row.Mod, $"Выключу повреждённый мод «{row.Name}» — его стоит скачать заново");
                    plan.Resolved.Add(issue);
                    break;

                case HealthIssueKind.CausesErrors:
                    foreach (var row in rows) DisableMod(row.Mod, $"Выключу «{row.Name}» — из-за него ошибки в игре");
                    plan.Resolved.Add(issue);
                    break;

                case HealthIssueKind.Conflict:
                    // Конфликт часто бывает нарочным (мод правит другой мод) — трогаем, только если ясно, что лишнее
                    if (rows.FirstOrDefault(r => r.IsSuggested)?.Mod is { } suggested)
                    {
                        DisableMod(suggested, $"Выключу «{suggested.Name}» — он мешает {Names(rows.Where(r => r.Mod != suggested))}");
                        plan.Resolved.Add(issue);
                    }
                    else plan.Manual.Add($"{issue.Title} — выберите сами, какой оставить");
                    break;

                case HealthIssueKind.UnknownErrors:
                    plan.HasUnknownErrors = true;
                    break;
            }
        }

        if (includeOutdated && HasGameUpdate)
        {
            plan.OutdatedScripts = true;
            plan.Lines.Add($"Выключу {Text.Plural(OutdatedScriptCount, "скрипт", "скрипта", "скриптов")}, которые могли сломаться после обновления игры");
        }
        return plan;

        static string Names(IEnumerable<HealthModRow> rows)
        {
            var names = rows.Select(r => $"«{r.Name}»").ToList();
            return names.Count <= 2 ? string.Join(" и ", names) : $"{names[0]} и ещё {names.Count - 1}";
        }
    }

    [RelayCommand]
    private Task FixAll() => RepairAsync(HealthIssues.ToList(), includeOutdated: true, title: null);

    /// <summary>Показывает план, после подтверждения исправляет. Возвращает true, если что-то исправили.</summary>
    private async Task<bool> RepairAsync(IReadOnlyList<HealthIssueViewModel> issues, bool includeOutdated, string? title)
    {
        if (_library is null) return false;
        var plan = BuildRepairPlan(issues, includeOutdated);
        if (plan.IsEmpty)
        {
            if (plan.HasUnknownErrors) await StartBisect();
            else ShowToast(plan.Manual.Count > 0 ? "Автоматически тут ничего не исправить — нужен ваш выбор" : "Исправлять нечего", Icons.Info);
            return false;
        }

        var lines = plan.Lines.Take(8).Select(l => "• " + l).ToList();
        if (plan.Lines.Count > 8) lines.Add($"• и ещё {plan.Lines.Count - 8}");
        var notes = new List<string> { "Моды не удаляются — только выключаются. Вернуть всё можно одной кнопкой «Отменить»." };
        if (plan.Manual.Count > 0) notes.Add("Не трону: " + string.Join("; ", plan.Manual.Take(3)) + ".");
        if (plan.HasUnknownErrors) notes.Add("Ошибки без явного виновника потом можно найти автоматически — «Найти виновника».");

        var answer = await ShowDialogAsync(new DialogViewModel
        {
            Title = title ?? "Вот что я исправлю",
            Icon = (System.Windows.Media.Geometry)System.Windows.Application.Current.FindResource("Icon.Wrench"),
            Message = string.Join(Environment.NewLine, lines),
            Message2 = string.Join(Environment.NewLine, notes),
            PrimaryText = "Исправить",
            SecondaryText = "Отмена",
        });
        if (answer != DialogResult.Primary || !await EnsureGameClosedAsync("исправить моды")) return false;

        var library = _library;
        var disabled = new List<ModItemViewModel>();
        var failed = 0;

        // 1. Выключаем лишнее и проблемное
        var toDisable = plan.Disable.Where(m => m.IsEnabled && Mods.Contains(m)).ToList();
        if (plan.OutdatedScripts) toDisable.AddRange(_outdatedScripts.Where(m => m.IsEnabled && Mods.Contains(m) && !toDisable.Contains(m)));
        var results = await Task.Run(() => toDisable.Select(m =>
        {
            try { return library.SetEnabled(m.Model, false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Interlocked.Increment(ref failed);
                return null;
            }
        }).ToList());
        for (var i = 0; i < toDisable.Count; i++)
        {
            if (results[i] is not { } updated) continue;
            toDisable[i].ReplaceModel(updated);
            disabled.Add(toDisable[i]);
        }
        if (plan.OutdatedScripts) AcknowledgeGameUpdate();

        // 2. Перекладываем файлы, которые лежат слишком глубоко
        foreach (var mod in plan.TooDeep.SelectMany(i => i.Mods).Select(r => r.Mod).Distinct())
        {
            try { mod.ReplaceModel(library.FixTooDeep(mod.Model)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
        }

        foreach (var issue in plan.Resolved) HealthIssues.Remove(issue);
        UpdateHealthCount();
        UpdateSummary();

        var fixedCount = plan.Lines.Count;
        ShowToast(failed > 0
                ? $"Исправлено, но {Text.Plural(failed, "файл", "файла", "файлов")} заняты другой программой"
                : $"Готово: {Text.Plural(fixedCount, "исправление", "исправления", "исправлений")}",
            Icons.Check,
            undo: disabled.Count == 0 ? null : () => _ = UndoRepairAsync(disabled),
            seconds: 8);
        SetLastAction("моды исправлены автоматически");

        // 3. Архивы — обычной установкой (сама покажет итог)
        if (plan.Archives.Count > 0)
        {
            var temp = Path.Combine(Path.GetTempPath(), "SimsModManager", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var copies = new List<string>();
            foreach (var archive in plan.Archives)
            {
                try
                {
                    var copy = Path.Combine(temp, Path.GetFileName(archive));
                    File.Move(archive, copy);
                    copies.Add(copy);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (copies.Count > 0) await InstallAsync(copies);
        }
        else
        {
            await RefreshAsync();
            _ = RunHealthCheck();
        }
        return true;
    }

    private async Task UndoRepairAsync(IReadOnlyList<ModItemViewModel> disabled)
    {
        if (_library is null || !await EnsureGameClosedAsync("вернуть моды")) return;
        var library = _library;
        var back = disabled.Where(m => !m.IsEnabled && Mods.Contains(m)).ToList();
        var results = await Task.Run(() => back.Select(m =>
        {
            try { return library.SetEnabled(m.Model, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
        }).ToList());
        for (var i = 0; i < back.Count; i++)
            if (results[i] is { } updated) back[i].ReplaceModel(updated);
        UpdateSummary();
        ShowToast("Моды включены обратно", Icons.Check);
        _ = RunHealthCheck();
    }
}
