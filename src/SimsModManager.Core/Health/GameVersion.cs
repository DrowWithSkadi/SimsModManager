using System.Text;
using System.Text.RegularExpressions;

namespace SimsModManager.Core.Health;

/// <summary>
/// Версия игры из файла GameVersion.txt, который игра обновляет в папке The Sims 4 при каждом запуске.
/// По ней понимаем, что игра обновилась и скриптовые моды могли сломаться.
/// </summary>
public static partial class GameVersion
{
    [GeneratedRegex(@"\d+\.\d+\.\d+(\.\d+)?")]
    private static partial Regex VersionPattern();

    public static string FilePath(SimsPaths paths) => Path.Combine(paths.Root, "GameVersion.txt");

    /// <summary>Версия вида «1.109.185.1030» или null, если игра ещё ни разу не запускалась.</summary>
    public static string? Read(SimsPaths paths)
    {
        try
        {
            var path = FilePath(paths);
            if (!File.Exists(path)) return null;
            // В начале файла бывают служебные байты — ищем саму версию.
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            var match = VersionPattern().Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Скриптовые моды, которые старше текущей версии игры и могут не работать.</summary>
    public static IReadOnlyList<ModItem> ScriptModsOlderThan(IEnumerable<ModItem> mods, DateTime updatedAt) =>
        mods.Where(m => m.IsEnabled && !m.IsTray && m.HasScripts && m.ModifiedAt < updatedAt).ToList();
}
