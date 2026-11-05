using System.IO.Compression;
using System.Text.RegularExpressions;
using SimsModManager.Core.Dbpf;

namespace SimsModManager.Core.Health;

public enum HealthIssueKind
{
    /// <summary>Два мода меняют одни и те же ресурсы игры.</summary>
    Conflict,
    /// <summary>В двух модах одинаковые части скрипта.</summary>
    ScriptConflict,
    /// <summary>Одни и те же файлы установлены дважды.</summary>
    Duplicate,
    /// <summary>Файлы лежат слишком глубоко в подпапках — игра их не видит.</summary>
    TooDeep,
    /// <summary>Файл повреждён (недокачан).</summary>
    Broken,
    /// <summary>Архив брошен в Mods нераспакованным.</summary>
    ArchiveInMods,
    /// <summary>Игра записала ошибки, и в них виноват этот мод.</summary>
    CausesErrors,
    /// <summary>Игра записала ошибки, но виновника по отчёту не определить.</summary>
    UnknownErrors,
}

public enum HealthSeverity { Info, Warning, Problem }

/// <summary>Найденная проблема, описанная обычными словами.</summary>
public sealed record HealthIssue(
    HealthIssueKind Kind,
    HealthSeverity Severity,
    string Title,
    string Explanation,
    string Advice,
    IReadOnlyList<ModItem> Mods)
{
    /// <summary>Мод, который лучше убрать (например, более старая версия), если это понятно.</summary>
    public ModItem? Suggested { get; init; }

    /// <summary>Файл, к которому относится проблема (архив, повреждённый файл).</summary>
    public string? FilePath { get; init; }

    /// <summary>Технические подробности — для тех, кто хочет разобраться или написать автору.</summary>
    public string? Details { get; init; }

    /// <summary>Сколько совпадений / отчётов — для заголовков.</summary>
    public int Count { get; init; }

    /// <summary>Постоянный ключ, чтобы можно было «не показывать снова».</summary>
    public string Key => Kind + ":" + (FilePath is not null
        ? Path.GetFileName(FilePath).ToLowerInvariant()
        : string.Join("|", Mods.Select(m => m.Name.ToLowerInvariant()).Order()));
}

public sealed record HealthReport(IReadOnlyList<HealthIssue> Issues, int CheckedMods, IReadOnlyList<string> ErrorReports)
{
    public int ProblemCount => Issues.Count(i => i.Severity != HealthSeverity.Info);
}

/// <summary>
/// Проверка модов: конфликты, дубликаты, неправильная установка, повреждённые файлы
/// и разбор отчётов об ошибках, которые пишет сама игра (lastException*.txt).
/// </summary>
public sealed class ModHealthChecker(SimsPaths paths)
{
    // Картинки-миниатюры часто совпадают у модов одного автора и ни на что не влияют.
    private static readonly HashSet<uint> IgnoredTypes =
    [
        0x3C1AF1F2, 0x3C2A8647, 0x5B282D45, 0xCD9DE247, 0x9C925813, 0x0D338A3A,
    ];

    private const uint CasPartType = 0x034AEECB;
    private const uint StringTableType = 0x220557DA;
    private static readonly HashSet<uint> ObjectTypes = [0xC0DB5AE7, 0x319E4F1D];
    private static readonly HashSet<uint> VisualTypes =
    [
        0x00B2D882, 0x3453CF95, 0xBA856C78, 0x2BC04EDF, 0x2F7D0004, // текстуры
        0x015A1849, 0x01661233, 0x01D10F34, 0xAC16FBEC, 0xD382BF57, // модели
    ];

    private enum Area { Texts, Visuals, Objects, Cas, Gameplay }

    public HealthReport Check(IReadOnlyList<ModItem> allMods, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var mods = allMods.Where(m => m.IsEnabled && !m.IsTray).ToList();
        var issues = new List<HealthIssue>();
        var broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        issues.AddRange(FindArchives());
        issues.AddRange(FindTooDeep(mods));

        var duplicatePairs = new HashSet<(ModItem, ModItem)>();
        issues.AddRange(FindDuplicates(mods, duplicatePairs));

        var modules = new Dictionary<ModItem, List<string>>();
        issues.AddRange(FindScriptConflicts(mods, duplicatePairs, modules, broken));
        ct.ThrowIfCancellationRequested();

        issues.AddRange(FindResourceConflicts(mods, duplicatePairs, broken, progress, ct));

        var reports = ErrorReportFiles();
        issues.AddRange(FindErrorCulprits(mods, modules, reports));

        var order = issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new HealthReport(order, mods.Count, reports);
    }

    // ---------- Архивы и глубина папок ----------

    private IEnumerable<HealthIssue> FindArchives()
    {
        if (!Directory.Exists(paths.Mods)) yield break;
        IEnumerable<string> archives;
        try
        {
            archives = Directory.EnumerateFiles(paths.Mods, "*", SearchOption.AllDirectories).Where(ModFiles.IsArchive).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var archive in archives)
        {
            yield return new HealthIssue(HealthIssueKind.ArchiveInMods, HealthSeverity.Problem,
                $"Архив «{Path.GetFileName(archive)}» не распакован",
                "Игра не умеет открывать архивы, поэтому моды из него сейчас не работают.",
                "Нажмите «Установить» — менеджер распакует архив, разложит файлы куда нужно и уберёт сам архив.",
                [])
            {
                FilePath = archive,
            };
        }
    }

    /// <summary>Скрипты игра ищет не глубже одной папки внутри Mods, остальное — не глубже пяти.</summary>
    public static IReadOnlyList<string> TooDeepFiles(ModItem mod, string modsRoot)
    {
        var result = new List<string>();
        foreach (var file in mod.Files)
        {
            var relative = Path.GetRelativePath(modsRoot, file);
            if (relative.StartsWith("..", StringComparison.Ordinal)) continue; // мод не в Mods
            var depth = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
            if (ModFiles.IsScript(file) ? depth > 1 : depth > 5) result.Add(file);
        }
        return result;
    }

    private IEnumerable<HealthIssue> FindTooDeep(List<ModItem> mods)
    {
        foreach (var mod in mods.Where(m => m.IsFolder))
        {
            var deep = TooDeepFiles(mod, paths.Mods);
            if (deep.Count == 0) continue;
            var scripts = deep.Any(ModFiles.IsScript);
            yield return new HealthIssue(HealthIssueKind.TooDeep, HealthSeverity.Problem,
                $"«{mod.Name}» не загружается — файлы слишком глубоко",
                scripts
                    ? "Игра ищет скрипты (.ts4script) не глубже одной папки внутри Mods. Этот мод лежит глубже, и игра его просто не видит."
                    : "Игра не заглядывает глубже пяти вложенных папок внутри Mods. Часть файлов этого мода лежит глубже, и игра их не видит.",
                "Нажмите «Исправить» — менеджер переложит файлы на правильный уровень.",
                [mod])
            {
                Count = deep.Count,
                Details = string.Join(Environment.NewLine, deep.Select(f => Path.GetRelativePath(paths.Mods, f))),
            };
        }
    }

    // ---------- Дубликаты ----------

    private static IEnumerable<HealthIssue> FindDuplicates(List<ModItem> mods, HashSet<(ModItem, ModItem)> pairs)
    {
        var byFile = new Dictionary<(string, long), List<ModItem>>();
        var fileCounts = new Dictionary<ModItem, int>();
        foreach (var mod in mods)
        {
            fileCounts[mod] = mod.Files.Count;
            foreach (var file in mod.Files)
            {
                long size;
                try { size = new FileInfo(file).Length; }
                catch (IOException) { continue; }
                var key = (Path.GetFileName(file).ToLowerInvariant(), size);
                if (!byFile.TryGetValue(key, out var list)) byFile[key] = list = [];
                if (!list.Contains(mod)) list.Add(mod);
            }
        }

        var shared = new Dictionary<(ModItem, ModItem), int>();
        foreach (var owners in byFile.Values.Where(o => o.Count > 1))
            for (var i = 0; i < owners.Count; i++)
            for (var j = i + 1; j < owners.Count; j++)
            {
                var pair = Order(owners[i], owners[j]);
                shared[pair] = shared.GetValueOrDefault(pair) + 1;
            }

        foreach (var ((a, b), count) in shared)
        {
            pairs.Add((a, b));
            var full = count >= Math.Min(fileCounts[a], fileCounts[b]);
            var older = a.ModifiedAt <= b.ModifiedAt ? a : b;
            yield return new HealthIssue(HealthIssueKind.Duplicate, full ? HealthSeverity.Problem : HealthSeverity.Warning,
                full ? $"Мод установлен дважды: «{a.Name}» и «{b.Name}»" : $"У «{a.Name}» и «{b.Name}» общие файлы",
                full
                    ? "Это один и тот же мод в двух местах. Две копии мешают друг другу и могут вызывать ошибки."
                    : $"В обоих модах лежат одинаковые файлы ({count}). Обычно так бывает, когда один мод уже включает в себя другой.",
                full ? $"Удалите одну копию — например, «{older.Name}»." : "Оставьте одну копию общих файлов: выключите или удалите тот мод, который нужен меньше.",
                [a, b])
            {
                Suggested = full ? older : null,
                Count = count,
            };
        }
    }

    // ---------- Скрипты ----------

    private static List<string> ReadModules(string scriptFile)
    {
        using var zip = ZipFile.OpenRead(scriptFile);
        return zip.Entries
            .Where(e => e.FullName.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .Select(e => NormalizeModule(e.FullName))
            .Distinct()
            .ToList();
    }

    private static string NormalizeModule(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        var dot = p.LastIndexOf('.');
        if (dot > p.LastIndexOf('/')) p = p[..dot];
        return p.TrimStart('/');
    }

    private IEnumerable<HealthIssue> FindScriptConflicts(List<ModItem> mods, HashSet<(ModItem, ModItem)> duplicates,
        Dictionary<ModItem, List<string>> modules, HashSet<string> broken)
    {
        var owners = new Dictionary<string, ModItem>();
        var shared = new Dictionary<(ModItem, ModItem), int>();
        var issues = new List<HealthIssue>();

        foreach (var mod in mods.Where(m => m.HasScripts))
        {
            var list = new List<string>();
            foreach (var script in mod.Files.Where(ModFiles.IsScript))
            {
                try
                {
                    list.AddRange(ReadModules(script));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    broken.Add(script);
                    issues.Add(BrokenIssue(mod, script));
                }
            }
            modules[mod] = list;
            foreach (var module in list.Distinct())
            {
                if (owners.TryGetValue(module, out var other) && other != mod)
                {
                    var pair = Order(other, mod);
                    shared[pair] = shared.GetValueOrDefault(pair) + 1;
                }
                else owners[module] = mod;
            }
        }

        foreach (var ((a, b), count) in shared)
        {
            if (duplicates.Contains((a, b))) continue;
            var (newer, older) = a.ModifiedAt >= b.ModifiedAt ? (a, b) : (b, a);
            var sameMod = LooksLikeSameMod(a.Name, b.Name);
            issues.Add(new HealthIssue(HealthIssueKind.ScriptConflict, HealthSeverity.Problem,
                $"«{a.Name}» и «{b.Name}» — похоже, один и тот же скрипт",
                $"В обоих модах есть одинаковые части скрипта ({count}). Обычно это две версии одного мода или мод, который уже встроен в другой. " +
                "Игра загрузит обе копии, и они будут мешать друг другу — частая причина ошибок и сломанных меню.",
                sameMod
                    ? $"Оставьте более новую версию — «{newer.Name}», а «{older.Name}» удалите."
                    : "Оставьте один из них. Если не знаете какой — выключите тот, что установлен раньше, и проверьте игру.",
                [a, b])
            {
                Suggested = older,
                Count = count,
            });
        }
        return issues;
    }

    // ---------- Пересечение ресурсов .package ----------

    private sealed class PairInfo
    {
        public int Count;
        public readonly Dictionary<Area, int> Areas = [];
    }

    private IEnumerable<HealthIssue> FindResourceConflicts(List<ModItem> mods, HashSet<(ModItem, ModItem)> duplicates,
        HashSet<string> broken, IProgress<double>? progress, CancellationToken ct)
    {
        var owners = new Dictionary<ResourceKey, (ModItem Mod, string File, ResourceEntry Entry)>();
        var candidates = new List<((ModItem Mod, string File, ResourceEntry Entry) First, (ModItem Mod, string File, ResourceEntry Entry) Second)>();
        var issues = new List<HealthIssue>();
        var done = 0;

        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in mod.Files.Where(ModFiles.IsPackage))
            {
                try
                {
                    using var package = DbpfPackage.Open(file);
                    foreach (var entry in package.Entries)
                    {
                        if (entry.IsDeleted || IgnoredTypes.Contains(entry.Key.Type)) continue;
                        if (owners.TryGetValue(entry.Key, out var first))
                        {
                            if (first.Mod != mod) candidates.Add((first, (mod, file, entry)));
                        }
                        else owners[entry.Key] = (mod, file, entry);
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
                                               or EndOfStreamException or ArgumentException or OverflowException)
                {
                    if (broken.Add(file)) issues.Add(BrokenIssue(mod, file));
                }
            }
            progress?.Report(++done / (double)Math.Max(1, mods.Count));
        }

        // Совпавшие ключи с одинаковым содержимым ни на что не влияют — сравниваем байты.
        var pairs = new Dictionary<(ModItem, ModItem), PairInfo>();
        foreach (var group in candidates.GroupBy(c => (c.First.File, c.Second.File)))
        {
            ct.ThrowIfCancellationRequested();
            DbpfPackage? p1 = null, p2 = null;
            try
            {
                foreach (var (first, second) in group)
                {
                    var pair = Order(first.Mod, second.Mod);
                    if (duplicates.Contains(pair)) continue;

                    var differ = first.Entry.StoredSize != second.Entry.StoredSize || first.Entry.MemorySize != second.Entry.MemorySize;
                    if (!differ)
                    {
                        p1 ??= DbpfPackage.Open(first.File);
                        p2 ??= DbpfPackage.Open(second.File);
                        differ = !p1.ReadRaw(first.Entry).AsSpan().SequenceEqual(p2.ReadRaw(second.Entry));
                    }
                    if (!differ) continue;

                    if (!pairs.TryGetValue(pair, out var info)) pairs[pair] = info = new PairInfo();
                    info.Count++;
                    var area = AreaOf(first.Entry.Key.Type);
                    info.Areas[area] = info.Areas.GetValueOrDefault(area) + 1;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Файл сменили или повредили во время проверки — пропускаем пару.
            }
            finally
            {
                p1?.Dispose();
                p2?.Dispose();
            }
        }

        foreach (var ((a, b), info) in pairs)
            issues.Add(ConflictIssue(a, b, info));
        return issues;
    }

    private static Area AreaOf(uint type) => type switch
    {
        CasPartType => Area.Cas,
        StringTableType => Area.Texts,
        _ when ObjectTypes.Contains(type) => Area.Objects,
        _ when VisualTypes.Contains(type) => Area.Visuals,
        _ => Area.Gameplay,
    };

    private static HealthIssue ConflictIssue(ModItem a, ModItem b, PairInfo info)
    {
        var main = info.Areas.Keys.Max();
        var (newer, older) = a.ModifiedAt >= b.ModifiedAt ? (a, b) : (b, a);
        var sameMod = LooksLikeSameMod(a.Name, b.Name);
        var n = info.Count;

        var (severity, explanation) = main switch
        {
            Area.Gameplay => (HealthSeverity.Problem,
                $"Оба мода меняют одни и те же игровые правила ({n} совпад.). Игра применит только один из них — второй сработает частично или не сработает. " +
                "Отсюда пропавшие действия, странное поведение симов или ошибки."),
            Area.Cas => (HealthSeverity.Warning,
                "Оба мода заменяют одни и те же вещи в редакторе персонажа. В игре будет видна только одна версия."),
            Area.Objects => (HealthSeverity.Warning,
                "Оба мода заменяют одни и те же объекты. В игре останется только одна версия."),
            Area.Visuals => (HealthSeverity.Warning,
                "Оба мода меняют одни и те же текстуры или модели (например, замена кожи, глаз или стандартных вещей). Видно будет только одну."),
            _ => (HealthSeverity.Info,
                "Оба мода меняют одни и те же надписи в игре. Обычно это безобидно, просто текст будет из одного мода."),
        };

        var advice = sameMod
            ? $"Похоже, это две версии одного мода. Оставьте более новую — «{newer.Name}», а «{older.Name}» удалите."
            : main == Area.Texts
                ? "Можно ничего не делать. Если какая-то надпись выглядит странно — выключите один из модов."
                : "Оставьте один из них — выключите тот, что нужен меньше.";

        return new HealthIssue(HealthIssueKind.Conflict, severity,
            $"«{a.Name}» и «{b.Name}» мешают друг другу",
            explanation, advice, [a, b])
        {
            Suggested = sameMod ? older : null,
            Count = n,
        };
    }

    private static HealthIssue BrokenIssue(ModItem mod, string file) =>
        new(HealthIssueKind.Broken, HealthSeverity.Problem,
            $"Файл мода «{mod.Name}» повреждён",
            $"Файл «{Path.GetFileName(file)}» не читается — скорее всего, он недокачался. Из-за таких файлов игра может зависнуть на загрузке или вылететь.",
            "Удалите мод и скачайте его заново у автора.",
            [mod])
        {
            FilePath = file,
        };

    // ---------- Отчёты об ошибках игры ----------

    /// <summary>Файлы, которые игра пишет при ошибках скриптов: lastException*.txt, lastUIException*.txt.</summary>
    public IReadOnlyList<string> ErrorReportFiles()
    {
        if (!Directory.Exists(paths.Root)) return [];
        try
        {
            return Directory.EnumerateFiles(paths.Root, "last*Exception*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static readonly Regex TracePath = new(@"File ""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ErrorLine = new(@"^\s*([A-Za-z_][\w.]*(?:Error|Exception)):\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Модули из пути в трассировке, например «...\Mods\X\x.ts4script\pkg\mod.py» → «pkg/mod».</summary>
    internal static ModItem? FindCulprit(string tracePath, IEnumerable<ModItem> mods, IReadOnlyDictionary<ModItem, List<string>> modules, string modsRoot)
    {
        var path = tracePath.Replace('/', '\\');
        // Код самой игры — не мод.
        if (path.Contains(@"\Gameplay\Scripts\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\InGame\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("<", StringComparison.Ordinal)) return null;

        // 1) Путь прямо указывает на файл в Mods
        var scriptIdx = path.IndexOf(".ts4script", StringComparison.OrdinalIgnoreCase);
        if (scriptIdx > 0)
        {
            var scriptName = Path.GetFileName(path[..(scriptIdx + ".ts4script".Length)]);
            var byFile = mods.FirstOrDefault(m => m.Files.Any(f => string.Equals(Path.GetFileName(f), scriptName, StringComparison.OrdinalIgnoreCase)));
            if (byFile is not null) return byFile;
        }

        // 2) По имени модуля: путь из компьютера автора заканчивается теми же папками, что и в архиве скрипта
        var normalized = NormalizeModule(path);
        ModItem? best = null;
        var bestLength = 0;
        foreach (var (mod, list) in modules)
        {
            foreach (var module in list)
            {
                if (!module.Contains('/')) continue; // одиночное имя файла слишком легко спутать
                if ((normalized == module || normalized.EndsWith("/" + module, StringComparison.Ordinal)) && module.Length > bestLength)
                {
                    best = mod;
                    bestLength = module.Length;
                }
            }
        }
        return best;
    }

    private IEnumerable<HealthIssue> FindErrorCulprits(List<ModItem> mods, Dictionary<ModItem, List<string>> modules, IReadOnlyList<string> reports)
    {
        if (reports.Count == 0) yield break;
        var byMod = new Dictionary<ModItem, (int Count, DateTime Last, string Error)>();
        var unknown = 0;
        var unknownLast = DateTime.MinValue;
        string? unknownError = null;

        foreach (var report in reports)
        {
            string text;
            DateTime time;
            try
            {
                text = File.ReadAllText(report);
                time = File.GetLastWriteTime(report);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var error = ErrorLine.Matches(text).Select(m => m.Value.Trim()).LastOrDefault() ?? "";
            var found = new HashSet<ModItem>();
            foreach (Match m in TracePath.Matches(text))
            {
                var culprit = FindCulprit(m.Groups[1].Value, mods, modules, paths.Mods);
                // Отчёт старше мода — мод уже обновили, этот отчёт к нему не относится.
                if (culprit is not null && time >= culprit.ModifiedAt) found.Add(culprit);
            }

            if (found.Count == 0)
            {
                unknown++;
                if (time > unknownLast) { unknownLast = time; unknownError = error; }
                continue;
            }
            foreach (var mod in found)
            {
                var cur = byMod.GetValueOrDefault(mod);
                byMod[mod] = (cur.Count + 1, time > cur.Last ? time : cur.Last, time > cur.Last ? error : cur.Error);
            }
        }

        foreach (var (mod, (count, last, error)) in byMod)
        {
            yield return new HealthIssue(HealthIssueKind.CausesErrors, HealthSeverity.Problem,
                $"«{mod.Name}» вызывает ошибки в игре",
                $"Игра записала {Text(count)}, и в них виноват этот мод (последний — {last:dd.MM.yyyy HH:mm}). " +
                "Чаще всего так бывает, когда мод устарел после обновления игры.",
                "Скачайте свежую версию у автора или выключите мод, пока она не выйдет.",
                [mod])
            {
                Count = count,
                Details = error,
            };
        }

        if (unknown > 0)
        {
            yield return new HealthIssue(HealthIssueKind.UnknownErrors, HealthSeverity.Warning,
                "В игре были ошибки, но виновник не ясен",
                $"Игра записала {Text(unknown)} (последний — {unknownLast:dd.MM.yyyy HH:mm}), но по ним нельзя точно сказать, какой мод виноват. " +
                "Иногда это ошибки самой игры.",
                "Если в игре что-то не работает — запустите «Найти виновника»: менеджер по очереди выключает половину модов и за несколько запусков находит нужный.",
                [])
            {
                Count = unknown,
                Details = unknownError,
            };
        }

        static string Text(int n) => n == 1 ? "отчёт об ошибке" : n is >= 2 and <= 4 ? $"{n} отчёта об ошибках" : $"{n} отчётов об ошибках";
    }

    // ---------- Общее ----------

    private static (ModItem, ModItem) Order(ModItem a, ModItem b) =>
        string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) <= 0 ? (a, b) : (b, a);

    /// <summary>«MCCC 2024.1» и «mc_cmd_center 2024.3» не узнаем, а «WickedWhims v170» и «WickedWhims_v172» — да.</summary>
    internal static bool LooksLikeSameMod(string a, string b)
    {
        static string Letters(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());
        var x = Letters(a);
        var y = Letters(b);
        if (x.Length < 4 || y.Length < 4) return false;
        if (x == y) return true;
        var common = 0;
        while (common < Math.Min(x.Length, y.Length) && x[common] == y[common]) common++;
        return common >= 6 || common >= Math.Min(x.Length, y.Length) * 0.8;
    }
}
