namespace SimsModManager.Core.Helper;

/// <summary>
/// Мод-помощник: маленький скриптовый мод, через который менеджер управляет игрой
/// (сохранить, пауза, команды). Лежит в Mods/SimsModManager и в списке модов не показывается.
/// </summary>
public static class HelperMod
{
    public const string FolderName = "SimsModManager";
    public const string FileName = "SimsModManagerHelper.ts4script";

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
        var target = InstalledFile(paths);
        if (File.Exists(target) && SameContent(target, bundledFile)) return false;

        Directory.CreateDirectory(Folder(paths));
        var temp = target + ".tmp";
        File.Copy(bundledFile, temp, overwrite: true);
        File.Move(temp, target, overwrite: true);
        return true;
    }

    public static void Uninstall(SimsPaths paths)
    {
        var folder = Folder(paths);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
