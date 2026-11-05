using SimsModManager.Core.Archives;

namespace SimsModManager.Core;

public enum InstallStatus { Installed, Updated, Failed, Skipped }

/// <summary>Ход установки: какой по счёту файл обрабатывается.</summary>
public readonly record struct InstallProgress(int Index, int Total, string CurrentName);

public sealed record InstallResult(
    string Source,
    string? ModName,
    InstallStatus Status,
    int FileCount,
    string? Message = null)
{
    /// <summary>Сколько файлов участка/комнаты/семьи ушло в папку Tray.</summary>
    public int TrayFileCount { get; init; }
    public TrayKind? TrayKind { get; init; }

    public bool Succeeded => Status is InstallStatus.Installed or InstallStatus.Updated;
}

/// <summary>
/// Устанавливает моды из архивов, папок и отдельных файлов.
/// Каждый мод кладётся в свою папку «Mods\Название мода\», все файлы — прямо в неё.
/// Так скрипты (.ts4script) никогда не оказываются глубже одной подпапки,
/// а иначе игра бы их не загрузила.
/// </summary>
public sealed class ModInstaller(SimsPaths paths, IArchiveExtractor extractor)
{
    private const int MaxNestedArchiveDepth = 3;

    /// <summary>Файлы настроек, которые некоторые скриптовые моды кладут рядом со скриптом.</summary>
    private static readonly string[] CompanionExtensions = [".cfg", ".ini", ".json"];

    public IReadOnlyList<InstallResult> Install(IEnumerable<string> sources, IProgress<InstallProgress>? progress = null)
    {
        var list = sources.ToList();
        var results = new List<InstallResult>();
        for (var i = 0; i < list.Count; i++)
        {
            progress?.Report(new InstallProgress(i, list.Count, Path.GetFileName(list[i])));
            results.Add(InstallOne(list[i]));
        }
        progress?.Report(new InstallProgress(list.Count, list.Count, ""));
        return results;
    }

    public InstallResult InstallOne(string source)
    {
        var isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source))
            return new(source, null, InstallStatus.Failed, 0, "Файл не найден.");

        if (!isDir && !ModFiles.IsArchive(source) && !ModFiles.IsModFile(source) && !ModFiles.IsTrayFile(source))
            return new(source, null, InstallStatus.Skipped, 0,
                "Это не мод и не архив (поддерживаются .zip, .rar, .7z, .package, .ts4script и файлы участков/семей).");

        var modName = ModFiles.SanitizeName(isDir
            ? new DirectoryInfo(source).Name
            : Path.GetFileNameWithoutExtension(source));

        var workDir = Path.Combine(Path.GetTempPath(), "SimsModManager", Guid.NewGuid().ToString("N"));
        try
        {
            var files = new List<string>();
            var trayFiles = new List<string>();
            if (isDir)
                Collect(source, files, trayFiles, workDir, 0);
            else if (ModFiles.IsModFile(source))
                files.Add(source);
            else if (ModFiles.IsTrayFile(source))
                trayFiles.Add(source);
            else
            {
                var extracted = Path.Combine(workDir, "a");
                extractor.ExtractAll(source, extracted);
                Collect(extracted, files, trayFiles, workDir, 1);
            }

            if (files.Count == 0 && trayFiles.Count == 0)
                return new(source, modName, InstallStatus.Failed, 0,
                    "Внутри нет ни модов (.package, .ts4script), ни участков или персонажей для «Библиотеки».");

            // Моды — в Mods\Название\, участки/комнаты/семьи — прямо в Tray.
            var updated = false;
            if (files.Count > 0) updated |= Place(modName, files);
            TrayKind? trayKind = null;
            if (trayFiles.Count > 0)
            {
                updated |= PlaceTray(modName, trayFiles);
                trayKind = ModFiles.DetectTrayKind(trayFiles);
            }

            return new(source, modName, updated ? InstallStatus.Updated : InstallStatus.Installed, files.Count)
            {
                TrayFileCount = trayFiles.Count,
                TrayKind = trayKind,
            };
        }
        catch (ArchiveExtractionException ex)
        {
            return new(source, modName, InstallStatus.Failed, 0, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(source, modName, InstallStatus.Failed, 0,
                "Не удалось записать файлы. Возможно, игра запущена и держит их открытыми. " + ex.Message);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    /// <summary>Находит файлы мода в папке, заодно распаковывая вложенные архивы.</summary>
    private void Collect(string dir, List<string> files, List<string> trayFiles, string workDir, int depth)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (IsJunk(file)) continue;

            if (ModFiles.IsModFile(file))
            {
                files.Add(file);
            }
            else if (ModFiles.IsTrayFile(file))
            {
                trayFiles.Add(file);
            }
            else if (ModFiles.IsArchive(file) && depth < MaxNestedArchiveDepth)
            {
                var nested = Path.Combine(workDir, "n" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    extractor.ExtractAll(file, nested);
                    Collect(nested, files, trayFiles, workDir, depth + 1);
                }
                catch (ArchiveExtractionException)
                {
                    // Битый вложенный архив не должен ломать установку остального.
                }
            }
            else if (IsCompanionFile(file))
            {
                files.Add(file);
            }
        }
    }

    private static bool IsJunk(string file)
    {
        var name = Path.GetFileName(file);
        return name.StartsWith("._", StringComparison.Ordinal)
            || file.Replace('\\', '/').Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Файл настроек, лежащий рядом со скриптом мода.</summary>
    private static bool IsCompanionFile(string file)
    {
        var ext = Path.GetExtension(file);
        if (!CompanionExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase))) return false;
        var folder = Path.GetDirectoryName(file)!;
        return Directory.EnumerateFiles(folder).Any(ModFiles.IsScript);
    }

    /// <summary>
    /// Копирует файлы во временную папку рядом с Mods и затем подменяет старую версию.
    /// Если что-то пойдёт не так при копировании, старая версия мода останется на месте.
    /// </summary>
    /// <returns>true, если мод с таким именем уже был и мы его обновили.</returns>
    private bool Place(string modName, List<string> files)
    {
        paths.EnsureCreated();
        var target = Path.Combine(paths.Mods, modName);
        var disabledCopy = Path.Combine(paths.DisabledMods, modName);
        var staging = Path.Combine(paths.Root, ".modmanager-staging", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);
        try
        {
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (used.TryGetValue(name, out var existing))
                {
                    // Тот же файл в двух местах архива — второй экземпляр не нужен.
                    if (new FileInfo(existing).Length == new FileInfo(file).Length) continue;
                    name = UniqueName(name, used);
                }
                used[name] = file;
                File.Copy(file, Path.Combine(staging, name));
            }

            var updated = Directory.Exists(target) || Directory.Exists(disabledCopy);

            DeleteEntry(target);
            DeleteEntry(disabledCopy);
            Directory.Move(staging, target);
            return updated;
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(Path.Combine(paths.Root, ".modmanager-staging"), onlyIfEmpty: true);
        }
    }

    /// <summary>
    /// Кладёт файлы участка/комнаты/семьи прямо в Tray (игра не смотрит в подпапки Tray)
    /// и запоминает их в списке, чтобы потом можно было выключить или удалить всё вместе.
    /// </summary>
    /// <returns>true, если такая загрузка уже была и мы её обновили.</returns>
    private bool PlaceTray(string name, List<string> trayFiles)
    {
        paths.EnsureTrayCreated();
        var manifest = TrayManifest.Load(paths);
        var existing = manifest.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        // Старые файлы прошлой версии убираем (и из Tray, и из выключенных).
        if (existing is not null)
        {
            foreach (var old in existing.Files)
            {
                TryDeleteFile(Path.Combine(paths.Tray, old));
                TryDeleteFile(Path.Combine(paths.DisabledTray, old));
            }
            manifest.Entries.Remove(existing);
        }

        var names = new List<string>();
        foreach (var file in trayFiles)
        {
            var fileName = Path.GetFileName(file);
            if (names.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(paths.Tray, fileName), overwrite: true);
            names.Add(fileName);

            // Если этот файл раньше числился за другой загрузкой — теперь он наш.
            foreach (var other in manifest.Entries)
                other.Files.RemoveAll(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        manifest.Entries.RemoveAll(e => e.Files.Count == 0);

        manifest.Entries.Add(new TrayEntry
        {
            Name = name,
            Kind = ModFiles.DetectTrayKind(names),
            Files = names,
            InstalledAt = DateTime.Now,
        });
        manifest.Save(paths);
        return existing is not null;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static string UniqueName(string name, Dictionary<string, string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!used.ContainsKey(candidate)) return candidate;
        }
    }

    private static void TryDeleteDirectory(string dir, bool onlyIfEmpty = false)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            if (onlyIfEmpty && Directory.EnumerateFileSystemEntries(dir).Any()) return;
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception)
        {
            // Временные файлы — не страшно, если не удалось убрать.
        }
    }
}
