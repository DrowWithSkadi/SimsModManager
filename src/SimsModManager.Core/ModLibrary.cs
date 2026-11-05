namespace SimsModManager.Core;

/// <summary>Один мод: папка внутри Mods или отдельный файл, лежащий прямо в Mods.</summary>
public sealed record ModItem(
    string Name,
    string Path,
    bool IsEnabled,
    bool IsFolder,
    IReadOnlyList<string> Files,
    long SizeBytes,
    DateTime ModifiedAt,
    IReadOnlyList<string> Problems)
{
    /// <summary>Когда мод появился в папке (для сортировки «по дате установки»).</summary>
    public DateTime InstalledAt { get; init; } = ModifiedAt;

    public int PackageCount => Files.Count(ModFiles.IsPackage);
    public int ScriptCount => Files.Count(ModFiles.IsScript);
    public bool HasScripts => ScriptCount > 0;
}

/// <summary>Удаление файлов. В программе — в корзину, в тестах — насовсем.</summary>
public interface IFileDeleter
{
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}

public sealed class PermanentDeleter : IFileDeleter
{
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);
}

public sealed class ModLibrary(SimsPaths paths, IFileDeleter? deleter = null)
{
    private readonly IFileDeleter _deleter = deleter ?? new PermanentDeleter();

    public SimsPaths Paths => paths;

    /// <summary>Список всех модов: включённых (в Mods) и выключенных.</summary>
    public IReadOnlyList<ModItem> Scan()
    {
        var items = new List<ModItem>();
        items.AddRange(ScanFolder(paths.Mods, enabled: true));
        items.AddRange(ScanFolder(paths.DisabledMods, enabled: false));
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private IEnumerable<ModItem> ScanFolder(string root, bool enabled)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = System.IO.Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;

            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(ModFiles.IsModFile)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (files.Count == 0) continue;

            var infos = files.Select(f => new FileInfo(f)).ToList();
            yield return new ModItem(
                name, dir, enabled, IsFolder: true, files,
                infos.Sum(i => i.Length),
                infos.Max(i => i.LastWriteTime),
                FindProblems(dir, files))
            {
                InstalledAt = Directory.GetCreationTime(dir),
            };
        }

        foreach (var file in Directory.EnumerateFiles(root).Where(ModFiles.IsModFile))
        {
            var info = new FileInfo(file);
            yield return new ModItem(
                System.IO.Path.GetFileNameWithoutExtension(file), file, enabled, IsFolder: false,
                [file], info.Length, info.LastWriteTime, [])
            {
                InstalledAt = info.CreationTime,
            };
        }
    }

    /// <summary>Частые ошибки ручной установки, из-за которых мод не работает.</summary>
    private static IReadOnlyList<string> FindProblems(string modDir, List<string> files)
    {
        var problems = new List<string>();
        // Скрипт должен лежать максимум в одной подпапке от Mods: Mods\Мод\script.ts4script
        var tooDeep = files.Where(ModFiles.IsScript)
            .Any(f => !string.Equals(System.IO.Path.GetDirectoryName(f), modDir, StringComparison.OrdinalIgnoreCase));
        if (tooDeep)
            problems.Add("Скрипт лежит слишком глубоко в подпапках, игра его не загрузит. Переустановите мод через менеджер.");
        return problems;
    }

    public ModItem SetEnabled(ModItem mod, bool enabled)
    {
        if (mod.IsEnabled == enabled) return mod;

        var targetRoot = enabled ? paths.Mods : paths.DisabledMods;
        Directory.CreateDirectory(targetRoot);
        var target = System.IO.Path.Combine(targetRoot, System.IO.Path.GetFileName(mod.Path));

        if (Directory.Exists(target) || File.Exists(target))
            throw new InvalidOperationException(
                $"Не получилось: в папке {(enabled ? "Mods" : "выключенных модов")} уже есть «{System.IO.Path.GetFileName(mod.Path)}».");

        if (mod.IsFolder) Directory.Move(mod.Path, target);
        else File.Move(mod.Path, target);

        var newFiles = mod.IsFolder
            ? mod.Files.Select(f => System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(mod.Path, f))).ToList()
            : [target];

        return mod with { Path = target, IsEnabled = enabled, Files = newFiles };
    }

    public void Delete(ModItem mod)
    {
        if (mod.IsFolder) _deleter.DeleteDirectory(mod.Path);
        else _deleter.DeleteFile(mod.Path);
    }

    // ---------- Удаление с возможностью отмены ----------

    private string TrashRoot => System.IO.Path.Combine(paths.Root, ".modmanager-trash");

    /// <summary>
    /// Убирает мод во временную папку рядом с игрой. Пока не вызван <see cref="FinishDelete"/>,
    /// удаление можно отменить через <see cref="UndoDelete"/>.
    /// </summary>
    public TrashedMod MoveToTrash(ModItem mod)
    {
        var holder = System.IO.Path.Combine(TrashRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(holder);
        var target = System.IO.Path.Combine(holder, System.IO.Path.GetFileName(mod.Path));
        if (mod.IsFolder) Directory.Move(mod.Path, target);
        else File.Move(mod.Path, target);
        return new TrashedMod(mod, target);
    }

    public void UndoDelete(TrashedMod trashed)
    {
        var original = trashed.Mod.Path;
        if (Directory.Exists(original) || File.Exists(original))
            throw new InvalidOperationException($"Не получилось вернуть: «{trashed.Mod.Name}» уже снова есть в папке.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(original)!);
        if (trashed.Mod.IsFolder) Directory.Move(trashed.TrashPath, original);
        else File.Move(trashed.TrashPath, original);
        CleanupHolder(trashed);
    }

    /// <summary>Окончательно удаляет мод (в корзину Windows или насовсем — зависит от IFileDeleter).</summary>
    public void FinishDelete(TrashedMod trashed)
    {
        if (trashed.Mod.IsFolder) _deleter.DeleteDirectory(trashed.TrashPath);
        else _deleter.DeleteFile(trashed.TrashPath);
        CleanupHolder(trashed);
    }

    /// <summary>Удаляет остатки, если программа закрылась, не успев завершить удаление.</summary>
    public void CleanupTrash()
    {
        if (!Directory.Exists(TrashRoot)) return;
        foreach (var holder in Directory.EnumerateDirectories(TrashRoot))
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(holder))
                {
                    if (Directory.Exists(entry)) _deleter.DeleteDirectory(entry);
                    else _deleter.DeleteFile(entry);
                }
                Directory.Delete(holder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        }
        TryDeleteEmpty(TrashRoot);
    }

    private void CleanupHolder(TrashedMod trashed)
    {
        TryDeleteEmpty(System.IO.Path.GetDirectoryName(trashed.TrashPath)!);
        TryDeleteEmpty(TrashRoot);
    }

    private static void TryDeleteEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- Дубликаты ----------

    /// <summary>
    /// Одинаковые файлы (то же имя и размер) в разных включённых модах.
    /// Игра загрузит оба, и это частая причина конфликтов.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> FindDuplicates(IEnumerable<ModItem> mods)
    {
        var byFile = new Dictionary<(string Name, long Size), List<ModItem>>();
        foreach (var mod in mods.Where(m => m.IsEnabled))
        {
            foreach (var file in mod.Files)
            {
                long size;
                try { size = new FileInfo(file).Length; }
                catch (IOException) { continue; }
                var key = (System.IO.Path.GetFileName(file).ToLowerInvariant(), size);
                if (!byFile.TryGetValue(key, out var list)) byFile[key] = list = [];
                if (!list.Contains(mod)) list.Add(mod);
            }
        }

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((name, _), owners) in byFile)
        {
            if (owners.Count < 2) continue;
            foreach (var mod in owners)
            {
                var others = string.Join(", ", owners.Where(o => o != mod).Select(o => $"«{o.Name}»"));
                if (!result.TryGetValue(mod.Path, out var msgs)) result[mod.Path] = msgs = [];
                msgs.Add($"Файл {name} есть также в {others}.");
            }
        }
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record TrashedMod(ModItem Mod, string TrashPath);
