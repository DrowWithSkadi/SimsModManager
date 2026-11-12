using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SimsModManager.Core;

/// <summary>
/// Проверки и мелкие действия с игрой: включены ли моды в настройках,
/// очистка кэша миниатюр, запущена ли игра.
/// </summary>
public static partial class GameOptions
{
    public sealed record ModSettingsState(bool OptionsFileFound, bool ModsEnabled, bool ScriptModsEnabled)
    {
        public bool AllGood => !OptionsFileFound || (ModsEnabled && ScriptModsEnabled);
    }

    [GeneratedRegex(@"^[ \t]*(modsdisabled|scriptmodsenabled)[ \t]*=[ \t]*(\d+)[ \t]*(?=\r?$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex OptionRegex();

    /// <summary>
    /// Читает Options.ini. Если игра ещё ни разу не запускалась с модами, строк может не быть —
    /// тогда считаем, что всё в порядке (игра сама спросит при запуске).
    /// </summary>
    public static ModSettingsState ReadModSettings(SimsPaths paths)
    {
        if (!File.Exists(paths.OptionsIni)) return new(false, true, true);

        string text;
        try { text = File.ReadAllText(paths.OptionsIni); }
        catch (IOException) { return new(false, true, true); }

        bool modsEnabled = true, scriptsEnabled = true;
        foreach (Match m in OptionRegex().Matches(text))
        {
            var value = m.Groups[2].Value;
            if (m.Groups[1].Value.Equals("modsdisabled", StringComparison.OrdinalIgnoreCase))
                modsEnabled = value == "0";
            else
                scriptsEnabled = value != "0";
        }
        return new(true, modsEnabled, scriptsEnabled);
    }

    /// <summary>Включает моды и скрипты в Options.ini. Игра в этот момент должна быть закрыта.</summary>
    public static void EnableMods(SimsPaths paths) => SetModSettings(paths, mods: true, scripts: true);

    /// <summary>
    /// Меняет настройки модов в Options.ini. null — оставить как есть.
    /// Игра в этот момент должна быть закрыта, иначе перезапишет файл при выходе.
    /// </summary>
    public static void SetModSettings(SimsPaths paths, bool? mods, bool? scripts)
    {
        if (!File.Exists(paths.OptionsIni)) return;
        var current = ReadModSettings(paths);
        var wantMods = mods ?? current.ModsEnabled;
        var wantScripts = scripts ?? current.ScriptModsEnabled;

        var text = File.ReadAllText(paths.OptionsIni);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        bool sawMods = false, sawScripts = false;

        text = OptionRegex().Replace(text, m =>
        {
            if (m.Groups[1].Value.Equals("modsdisabled", StringComparison.OrdinalIgnoreCase))
            {
                sawMods = true;
                return "modsdisabled = " + (wantMods ? "0" : "1");
            }
            sawScripts = true;
            return "scriptmodsenabled = " + (wantScripts ? "1" : "0");
        });

        // Если строк не было — добавляем их в секцию [options]
        var additions = (sawMods ? "" : "modsdisabled = " + (wantMods ? "0" : "1") + newline)
                        + (sawScripts ? "" : "scriptmodsenabled = " + (wantScripts ? "1" : "0") + newline);
        if (additions.Length > 0)
        {
            var idx = text.IndexOf("[options]", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var lineEnd = text.IndexOf('\n', idx);
                text = lineEnd < 0
                    ? text + newline + additions
                    : text.Insert(lineEnd + 1, additions);
            }
            else
            {
                text = "[options]" + newline + additions + text;
            }
        }

        File.Copy(paths.OptionsIni, paths.OptionsIni + ".bak", overwrite: true);
        File.WriteAllText(paths.OptionsIni, text);
    }

    /// <summary>
    /// Удаляет localthumbcache.package. Игра пересоздаст его сама; это помогает,
    /// когда после установки или удаления модов в игре видны старые или битые вещи.
    /// </summary>
    public static bool ClearThumbnailCache(SimsPaths paths)
    {
        if (!File.Exists(paths.ThumbnailCache)) return false;
        File.Delete(paths.ThumbnailCache);
        return true;
    }

    public static readonly string[] GameProcessNames = ["TS4_x64", "TS4_DX9_x64", "TS4"];

    public static bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (running) return true;
        }
        return false;
    }
}
