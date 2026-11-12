namespace SimsModManager.Core;

public enum ModKind { Mod, Tray }

/// <summary>
/// Один элемент списка: мод (папка или файл в Mods) либо участок/комната/семья в Tray.
/// </summary>
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

    public ModKind Kind { get; init; } = ModKind.Mod;
    public TrayKind TrayKind { get; init; }
    /// <summary>Номер записи в списке Tray (для участков и семей).</summary>
    public string? TrayId { get; init; }
    public bool IsTray => Kind == ModKind.Tray;

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
        items.AddRange(ScanTray());
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private IEnumerable<ModItem> ScanFolder(string root, bool enabled)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = System.IO.Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            // Мод-помощник менеджера в списке не показываем — им управляет сама программа
            if (string.Equals(name, Helper.HelperMod.FolderName, StringComparison.OrdinalIgnoreCase)) continue;

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

    /// <summary>Участки, комнаты и семьи, установленные через менеджер.</summary>
    private IEnumerable<ModItem> ScanTray()
    {
        if (!File.Exists(paths.TrayManifest)) return [];
        var manifest = TrayManifest.Load(paths);
        var result = new List<ModItem>();
        var changed = false;

        foreach (var entry in manifest.Entries.ToList())
        {
            var folder = entry.Enabled ? paths.Tray : paths.DisabledTray;
            var files = entry.Files.Select(f => System.IO.Path.Combine(folder, f)).Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                // Файлы удалили вручную — забываем запись.
                manifest.Entries.Remove(entry);
                changed = true;
                continue;
            }
            var infos = files.Select(f => new FileInfo(f)).ToList();
            result.Add(new ModItem(entry.Name, TrayPath(entry.Id), entry.Enabled, IsFolder: false, files,
                infos.Sum(i => i.Length), infos.Max(i => i.LastWriteTime), [])
            {
                InstalledAt = entry.InstalledAt,
                Kind = ModKind.Tray,
                TrayKind = entry.Kind,
                TrayId = entry.Id,
            });
        }

        if (changed)
        {
            try { manifest.Save(paths); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>Условный «путь» элемента Tray — по нему элементы различаются в списке.</summary>
    private string TrayPath(string id) => System.IO.Path.Combine(paths.Tray, "#" + id);

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
        if (mod.IsTray) return SetTrayEnabled(mod, enabled);

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

    /// <summary>
    /// Переложить файлы, которые лежат слишком глубоко (игра их не видит), в верхнюю папку мода.
    /// Возвращает обновлённый мод.
    /// </summary>
    public ModItem FixTooDeep(ModItem mod)
    {
        if (!mod.IsFolder || mod.IsTray) return mod;
        var root = mod.IsEnabled ? paths.Mods : paths.DisabledMods;
        var deep = Health.ModHealthChecker.TooDeepFiles(mod, root);
        if (deep.Count == 0) return mod;

        var files = mod.Files.ToList();
        foreach (var file in deep)
        {
            var target = System.IO.Path.Combine(mod.Path, System.IO.Path.GetFileName(file));
            var n = 2;
            while (File.Exists(target))
                target = System.IO.Path.Combine(mod.Path,
                    $"{System.IO.Path.GetFileNameWithoutExtension(file)} ({n++}){System.IO.Path.GetExtension(file)}");
            File.Move(file, target);
            files[files.IndexOf(file)] = target;
        }

        // Пустые подпапки после переноса убираем.
        foreach (var dir in Directory.EnumerateDirectories(mod.Path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { }
        }
        return mod with { Files = files, Problems = [] };
    }

    /// <summary>Переименовать мод: папку или файл в Mods либо запись участка/семьи.</summary>
    public ModItem Rename(ModItem mod, string newName)
    {
        var clean = ModFiles.SanitizeName(newName);
        if (clean.Equals(mod.Name, StringComparison.Ordinal)) return mod;

        if (mod.IsTray)
        {
            var manifest = TrayManifest.Load(paths);
            var entry = manifest.Entries.FirstOrDefault(e => e.Id == mod.TrayId)
                        ?? throw new InvalidOperationException("Запись не найдена — обновите список.");
            entry.Name = clean;
            manifest.Save(paths);
            return mod with { Name = clean };
        }

        var folder = System.IO.Path.GetDirectoryName(mod.Path)!;
        var target = mod.IsFolder
            ? System.IO.Path.Combine(folder, clean)
            : System.IO.Path.Combine(folder, clean + System.IO.Path.GetExtension(mod.Path));
        var sameIgnoringCase = string.Equals(target, mod.Path, StringComparison.OrdinalIgnoreCase);
        if (!sameIgnoringCase && (Directory.Exists(target) || File.Exists(target)))
            throw new InvalidOperationException($"Мод с именем «{clean}» уже есть.");

        if (mod.IsFolder)
        {
            if (sameIgnoringCase)
            {
                // Windows не различает регистр — переименовываем через временное имя
                var tmp = target + ".smm-rename";
                Directory.Move(mod.Path, tmp);
                Directory.Move(tmp, target);
            }
            else Directory.Move(mod.Path, target);
            var files = mod.Files.Select(f => System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(mod.Path, f))).ToList();
            return mod with { Name = clean, Path = target, Files = files };
        }

        File.Move(mod.Path, target);
        return mod with { Name = clean, Path = target, Files = [target] };
    }

    /// <summary>Выключенный участок/семья переезжает в «Tray (выключенные)» — игра его не видит.</summary>
    private ModItem SetTrayEnabled(ModItem mod, bool enabled)
    {
        paths.EnsureTrayCreated();
        var targetRoot = enabled ? paths.Tray : paths.DisabledTray;
        var conflict = mod.Files.FirstOrDefault(f => File.Exists(System.IO.Path.Combine(targetRoot, System.IO.Path.GetFileName(f))));
        if (conflict is not null)
            throw new InvalidOperationException($"В папке {(enabled ? "Tray" : "выключенных")} уже есть файл {System.IO.Path.GetFileName(conflict)}.");

        var moved = new List<string>();
        foreach (var file in mod.Files)
        {
            var target = System.IO.Path.Combine(targetRoot, System.IO.Path.GetFileName(file));
            File.Move(file, target);
            moved.Add(target);
        }

        var manifest = TrayManifest.Load(paths);
        var entry = manifest.Entries.FirstOrDefault(e => e.Id == mod.TrayId);
        if (entry is not null)
        {
            entry.Enabled = enabled;
            manifest.Save(paths);
        }
        return mod with { IsEnabled = enabled, Files = moved };
    }

    public void Delete(ModItem mod)
    {
        if (mod.IsTray)
        {
            foreach (var f in mod.Files) _deleter.DeleteFile(f);
            RemoveTrayEntry(mod.TrayId);
            return;
        }
        if (mod.IsFolder) _deleter.DeleteDirectory(mod.Path);
        else _deleter.DeleteFile(mod.Path);
    }

    private TrayEntry? RemoveTrayEntry(string? id)
    {
        var manifest = TrayManifest.Load(paths);
        var entry = manifest.Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return null;
        manifest.Entries.Remove(entry);
        manifest.Save(paths);
        return entry;
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

        if (mod.IsTray)
        {
            // Папка с понятным именем — так элемент и окажется в корзине Windows.
            var box = System.IO.Path.Combine(holder, ModFiles.SanitizeName(mod.Name));
            Directory.CreateDirectory(box);
            foreach (var file in mod.Files)
                File.Move(file, System.IO.Path.Combine(box, System.IO.Path.GetFileName(file)));
            return new TrashedMod(mod, box) { TrayEntry = RemoveTrayEntry(mod.TrayId) };
        }

        var target = System.IO.Path.Combine(holder, System.IO.Path.GetFileName(mod.Path));
        if (mod.IsFolder) Directory.Move(mod.Path, target);
        else File.Move(mod.Path, target);
        return new TrashedMod(mod, target);
    }

    public void UndoDelete(TrashedMod trashed)
    {
        if (trashed.Mod.IsTray)
        {
            UndoTrayDelete(trashed);
            return;
        }
        var original = trashed.Mod.Path;
        if (Directory.Exists(original) || File.Exists(original))
            throw new InvalidOperationException($"Не получилось вернуть: «{trashed.Mod.Name}» уже снова есть в папке.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(original)!);
        if (trashed.Mod.IsFolder) Directory.Move(trashed.TrashPath, original);
        else File.Move(trashed.TrashPath, original);
        CleanupHolder(trashed);
    }

    private void UndoTrayDelete(TrashedMod trashed)
    {
        paths.EnsureTrayCreated();
        foreach (var file in trashed.Mod.Files)
        {
            var name = System.IO.Path.GetFileName(file);
            var from = System.IO.Path.Combine(trashed.TrashPath, name);
            if (!File.Exists(from)) continue;
            if (File.Exists(file))
                throw new InvalidOperationException($"Не получилось вернуть: файл {name} уже снова есть в папке.");
            File.Move(from, file);
        }
        if (trashed.TrayEntry is not null)
        {
            var manifest = TrayManifest.Load(paths);
            manifest.Entries.Add(trashed.TrayEntry);
            manifest.Save(paths);
        }
        TryDeleteEmpty(trashed.TrashPath);
        CleanupHolder(trashed);
    }

    /// <summary>Окончательно удаляет мод (в корзину Windows или насовсем — зависит от IFileDeleter).</summary>
    public void FinishDelete(TrashedMod trashed)
    {
        if (trashed.Mod.IsTray)
        {
            if (Directory.Exists(trashed.TrashPath)) _deleter.DeleteDirectory(trashed.TrashPath);
            CleanupHolder(trashed);
            return;
        }
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
        foreach (var mod in mods.Where(m => m.IsEnabled && !m.IsTray))
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

public sealed record TrashedMod(ModItem Mod, string TrashPath)
{
    /// <summary>Для участков и семей — запись из списка Tray, чтобы вернуть её при отмене.</summary>
    public TrayEntry? TrayEntry { get; init; }
}
