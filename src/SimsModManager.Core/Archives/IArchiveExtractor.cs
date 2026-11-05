namespace SimsModManager.Core.Archives;

public interface IArchiveExtractor
{
    /// <summary>Распаковывает архив целиком в папку <paramref name="destination"/>.</summary>
    void ExtractAll(string archivePath, string destination);
}

public sealed class ArchiveExtractionException(string message, Exception? inner = null) : Exception(message, inner);

internal static class SafePath
{
    /// <summary>
    /// Склеивает путь внутри архива с папкой назначения и не даёт «выйти» за её пределы
    /// (защита от архивов с путями вида ..\..\Windows\file).
    /// </summary>
    public static string? Resolve(string destination, string entryKey)
    {
        var root = Path.GetFullPath(destination);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

        var relative = entryKey.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0) return null;

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
