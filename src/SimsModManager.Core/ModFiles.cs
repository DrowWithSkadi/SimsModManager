namespace SimsModManager.Core;

/// <summary>Какие файлы считаются модами и какие архивы мы умеем распаковывать.</summary>
public static class ModFiles
{
    public const string PackageExtension = ".package";
    public const string ScriptExtension = ".ts4script";

    public static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

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
