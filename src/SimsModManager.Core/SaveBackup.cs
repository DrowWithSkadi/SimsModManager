using System.IO.Compression;

namespace SimsModManager.Core;

/// <summary>
/// Резервные копии сохранений: перед запуском игры папка saves упаковывается в zip.
/// Хранятся последние несколько копий; если с прошлой копии ничего не изменилось — новая не делается.
/// </summary>
public static class SaveBackup
{
    public const int DefaultKeep = 10;

    public static string DefaultFolder => AppIdentity.Roaming("SaveBackups");

    /// <summary>Копии, самые новые первыми.</summary>
    public static IReadOnlyList<string> List(string backupFolder) =>
        Directory.Exists(backupFolder)
            ? Directory.GetFiles(backupFolder, "saves-*.zip").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>Делает копию. Возвращает путь к копии (новой или прежней, если изменений не было) или null, если сохранений нет.</summary>
    public static string? Create(SimsPaths paths, string backupFolder, int keep = DefaultKeep, DateTime? now = null)
    {
        if (!Directory.Exists(paths.Saves)) return null;
        var files = Directory.EnumerateFiles(paths.Saves, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) return null;

        Directory.CreateDirectory(backupFolder);
        var latestChange = files.Max(File.GetLastWriteTimeUtc);
        var existing = List(backupFolder);
        if (existing.Count > 0 && File.GetLastWriteTimeUtc(existing[0]) >= latestChange) return existing[0];

        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm-ss");
        var target = Path.Combine(backupFolder, $"saves-{stamp}.zip");
        var temp = target + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        ZipFile.CreateFromDirectory(paths.Saves, temp, CompressionLevel.Fastest, includeBaseDirectory: false);
        File.Move(temp, target, overwrite: true);

        foreach (var old in List(backupFolder).Skip(keep))
        {
            try { File.Delete(old); }
            catch (IOException) { }
        }
        return target;
    }
}
