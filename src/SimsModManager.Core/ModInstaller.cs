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

        if (!isDir && !ModFiles.IsArchive(source) && !ModFiles.IsModFile(source))
            return new(source, null, InstallStatus.Skipped, 0,
                "Это не мод и не архив (поддерживаются .zip, .rar, .7z, .package, .ts4script).");

        var modName = ModFiles.SanitizeName(isDir
            ? new DirectoryInfo(source).Name
            : Path.GetFileNameWithoutExtension(source));

        var workDir = Path.Combine(Path.GetTempPath(), "SimsModManager", Guid.NewGuid().ToString("N"));
        try
        {
            var files = new List<string>();
            if (isDir)
                Collect(source, files, workDir, 0);
            else if (ModFiles.IsModFile(source))
                files.Add(source);
            else
            {
                var extracted = Path.Combine(workDir, "a");
                extractor.ExtractAll(source, extracted);
                Collect(extracted, files, workDir, 1);
            }

            if (files.Count == 0)
                return new(source, modName, InstallStatus.Failed, 0, "Внутри нет файлов модов (.package или .ts4script).");

            var updated = Place(modName, files);
            return new(source, modName, updated ? InstallStatus.Updated : InstallStatus.Installed, files.Count);
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
    private void Collect(string dir, List<string> files, string workDir, int depth)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (IsJunk(file)) continue;

            if (ModFiles.IsModFile(file))
            {
                files.Add(file);
            }
            else if (ModFiles.IsArchive(file) && depth < MaxNestedArchiveDepth)
            {
                var nested = Path.Combine(workDir, "n" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    extractor.ExtractAll(file, nested);
                    Collect(nested, files, workDir, depth + 1);
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
