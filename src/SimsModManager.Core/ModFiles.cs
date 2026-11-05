namespace SimsModManager.Core;

/// <summary>Элемент «Библиотеки» игры: участок, комната или семья (персонажи).</summary>
public enum TrayKind { Mixed, Lot, Room, Household }

/// <summary>Какие файлы считаются модами и какие архивы мы умеем распаковывать.</summary>
public static class ModFiles
{
    public const string PackageExtension = ".package";
    public const string ScriptExtension = ".ts4script";

    public static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    /// <summary>
    /// Файлы из «Библиотеки» игры: участки, комнаты, семьи и персонажи.
    /// Они кладутся не в Mods, а прямо в папку Tray (без подпапок).
    /// </summary>
    public static readonly string[] TrayExtensions =
        [".trayitem", ".blueprint", ".bpi", ".room", ".rmi", ".householdbinary", ".hhi", ".sgi"];

    public static bool IsTrayFile(string path)
    {
        var ext = Path.GetExtension(path);
        return TrayExtensions.Any(t => t.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Что за элемент «Библиотеки» по набору файлов.</summary>
    public static TrayKind DetectTrayKind(IEnumerable<string> files)
    {
        var exts = files.Select(f => Path.GetExtension(f).ToLowerInvariant()).ToHashSet();
        var lot = exts.Contains(".blueprint") || exts.Contains(".bpi");
        var room = exts.Contains(".room") || exts.Contains(".rmi");
        var household = exts.Contains(".householdbinary") || exts.Contains(".hhi") || exts.Contains(".sgi");
        var count = (lot ? 1 : 0) + (room ? 1 : 0) + (household ? 1 : 0);
        if (count > 1) return TrayKind.Mixed;
        if (lot) return TrayKind.Lot;
        if (room) return TrayKind.Room;
        if (household) return TrayKind.Household;
        return TrayKind.Mixed;
    }

    public static string TrayKindName(TrayKind kind) => kind switch
    {
        TrayKind.Lot => "Участок",
        TrayKind.Room => "Комната",
        TrayKind.Household => "Семья",
        _ => "Библиотека",
    };

    public static bool IsModFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(PackageExtension, StringComparison.OrdinalIgnoreCase)
            || ext.Equals(ScriptExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPackage(string path) =>
        Path.GetExtension(path).Equals(PackageExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsScript(string path) =>
        Path.GetExtension(path).Equals(ScriptExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ArchiveExtensions.Any(a => a.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Убирает из имени символы, которые нельзя использовать в названии папки Windows.</summary>
    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "Mod" : result;
    }
}
