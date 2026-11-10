using System.Text.Json;

namespace SimsModManager.Core;

/// <summary>Настройки программы, хранятся в %AppData%\SimsModManager\settings.json.</summary>
public sealed class AppSettings
{
    public string? SimsFolder { get; set; }
    public bool AutoClearCache { get; set; } = true;
    public bool DeleteToRecycleBin { get; set; } = true;
    public bool GridView { get; set; }
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
    /// <summary>Тема оформления: Dark, Light или Glass.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Показывать анимацию с логотипом при запуске.</summary>
    public bool ShowSplash { get; set; } = true;

    /// <summary>Показывать Коробка — помощника в шапке.</summary>
    public bool ShowMascot { get; set; } = true;

    /// <summary>Путь к TS4_x64.exe, если пользователь указал его вручную.</summary>
    public string? GameExe { get; set; }

    /// <summary>
    /// «Играть без модов/скриптов» временно меняет Options.ini. Здесь — как было до этого,
    /// чтобы вернуть, когда игра закроется (даже если менеджер перезапускали).
    /// </summary>
    public bool? RestoreModsEnabled { get; set; }
    public bool? RestoreScriptsEnabled { get; set; }

    /// <summary>Версия игры, после которой пользователь подтвердил, что моды работают.</summary>
    public string? AcknowledgedGameVersion { get; set; }

    /// <summary>Проблемы, которые пользователь попросил больше не показывать.</summary>
    public List<string> IgnoredIssues { get; set; } = [];

    /// <summary>Когда заметили новую версию игры — скрипты старше этого момента могли сломаться.</summary>
    public DateTime? GameUpdateDetectedAt { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimsModManager", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Испорченный файл настроек — просто начинаем с чистого листа.
        }
        return new();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
