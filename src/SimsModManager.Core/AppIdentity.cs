namespace SimsModManager.Core;

/// <summary>
/// Имя программы и её папки. Раньше программа называлась Sims Mod Manager и жила в папках SimsModManager —
/// при первом запуске новой версии они переносятся (<see cref="MigrateLegacyFolders"/>).
/// </summary>
public static class AppIdentity
{
    public const string Name = "Ancora";
    public const string FolderName = "Ancora";

    public const string LegacyName = "Sims Mod Manager";
    public const string LegacyFolderName = "SimsModManager";

    /// <summary>Что из %LocalAppData%\SimsModManager наше. Остальное там — файлы установщика, их не трогаем.</summary>
    internal static readonly string[] LegacyLocalEntries = ["cache", "originals"];

    private static string RoamingRoot => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalRoot => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%AppData%\Ancora\… — настройки, точки сохранения, копии сохранений.</summary>
    public static string Roaming(params string[] parts) => Path.Combine([RoamingRoot, FolderName, .. parts]);

    /// <summary>%LocalAppData%\Ancora\… — кэш превью и авторские копии изменённых файлов.</summary>
    public static string Local(params string[] parts) => Path.Combine([LocalRoot, FolderName, .. parts]);

    /// <summary>Временная папка программы для одной операции.</summary>
    public static string NewTempFolder() => Path.Combine(Path.GetTempPath(), FolderName, Guid.NewGuid().ToString("N"));

    /// <summary>Переносит данные из старых папок SimsModManager в папки Ancora.</summary>
    public static IReadOnlyList<string> MigrateLegacyFolders() => MigrateLegacyFolders(RoamingRoot, LocalRoot);

    /// <summary>
    /// Переносит по одному элементу: если что-то занято другой программой, остальное всё равно переедет,
    /// а оставшееся перенесётся при следующем запуске. То, что уже есть в новой папке, не перезаписывается.
    /// Возвращает описания того, что перенести не удалось.
    /// </summary>
    public static IReadOnlyList<string> MigrateLegacyFolders(string roamingRoot, string localRoot)
    {
        var problems = new List<string>();

        var oldRoaming = Path.Combine(roamingRoot, LegacyFolderName);
        var newRoaming = Path.Combine(roamingRoot, FolderName);
        if (Directory.Exists(oldRoaming))
        {
            if (!Directory.Exists(newRoaming))
            {
                try { Directory.Move(oldRoaming, newRoaming); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MoveEntries(oldRoaming, newRoaming, null, problems); }
            }
            else MoveEntries(oldRoaming, newRoaming, null, problems);
            TryDeleteIfEmpty(oldRoaming);
        }

        var oldLocal = Path.Combine(localRoot, LegacyFolderName);
        if (Directory.Exists(oldLocal))
            MoveEntries(oldLocal, Path.Combine(localRoot, FolderName), LegacyLocalEntries, problems);

        return problems;
    }

    private static void MoveEntries(string from, string to, string[]? only, List<string> problems)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(from).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(from + ": " + ex.Message); return; }

        foreach (var source in entries)
        {
            var name = Path.GetFileName(source);
            if (only is not null && !only.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(to, name);
            try
            {
                Directory.CreateDirectory(to);
                if (Directory.Exists(source))
                {
                    if (!Directory.Exists(target)) Directory.Move(source, target);
                    else MoveEntries(source, target, null, problems); // папка уже есть — переносим то, чего в ней нет
                    TryDeleteIfEmpty(source);
                }
                else if (!File.Exists(target)) File.Move(source, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(source + ": " + ex.Message);
            }
        }
    }

    private static void TryDeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
