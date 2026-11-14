namespace SimsModManager.Core.Helper;

/// <summary>
/// Мод-помощник: маленький скриптовый мод, через который менеджер управляет игрой
/// (сохранить, пауза, команды). Лежит в Mods/Ancora и в списке модов не показывается.
/// До переименования программы он лежал в Mods/SimsModManager — эту папку при установке убираем,
/// иначе игра загрузила бы двух помощников сразу.
/// </summary>
public static class HelperMod
{
    public const string FolderName = AppIdentity.FolderName;
    public const string FileName = "AncoraHelper.ts4script";
    public const string LegacyFolderName = AppIdentity.LegacyFolderName;

    /// <summary>Папки помощника (новая и старая) — в списке модов их не показываем.</summary>
    public static bool IsHelperFolderName(string name) =>
        string.Equals(name, FolderName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, LegacyFolderName, StringComparison.OrdinalIgnoreCase);

    public static string Folder(SimsPaths paths) => Path.Combine(paths.Mods, FolderName);
    public static string InstalledFile(SimsPaths paths) => Path.Combine(Folder(paths), FileName);
    public static string BridgeFolder(SimsPaths paths) => Path.Combine(Folder(paths), "bridge");

    public static bool IsInstalled(SimsPaths paths) => File.Exists(InstalledFile(paths));

    /// <summary>
    /// Ставит или обновляет помощника из файла, который идёт вместе с программой.
    /// Возвращает true, если файл изменился (игре нужен перезапуск, чтобы его подхватить).
    /// </summary>
    public static bool Install(SimsPaths paths, string bundledFile)
    {
        if (!File.Exists(bundledFile)) return false;
        // Сначала старый помощник: если игра держит его открытым, бросится IOException и новый не встанет —
        // попробуем в следующий раз, зато в игре не окажется двух помощников одновременно.
        var legacyRemoved = RemoveLegacy(paths);
        var target = InstalledFile(paths);
        if (File.Exists(target) && SameContent(target, bundledFile)) return legacyRemoved;

        Directory.CreateDirectory(Folder(paths));
        var temp = target + ".tmp";
        File.Copy(bundledFile, temp, overwrite: true);
        File.Move(temp, target, overwrite: true);
        return true;
    }

    public static void Uninstall(SimsPaths paths)
    {
        RemoveLegacy(paths);
        var folder = Folder(paths);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static bool RemoveLegacy(SimsPaths paths)
    {
        var legacy = Path.Combine(paths.Mods, LegacyFolderName);
        if (!Directory.Exists(legacy)) return false;
        Directory.Delete(legacy, recursive: true);
        return true;
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
