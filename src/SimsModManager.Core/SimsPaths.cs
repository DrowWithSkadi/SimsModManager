namespace SimsModManager.Core;

/// <summary>Пути внутри пользовательской папки игры.</summary>
public sealed class SimsPaths(string simsFolder)
{
    /// <summary>
    /// Папки для выключенных модов и участков лежат рядом с Mods и Tray, поэтому игра их не видит.
    /// Имя зависит от языка, но только при создании: если папка уже есть (на любом языке),
    /// берём её — смена языка ничего не переносит и не плодит вторую папку.
    /// </summary>
    public static readonly (string Ru, string En) DisabledModsNames = ("Mods (выключенные)", "Mods (disabled)");
    public static readonly (string Ru, string En) DisabledTrayNames = ("Tray (выключенные)", "Tray (disabled)");

    public string Root { get; } = Path.GetFullPath(simsFolder);
    public string Mods => Path.Combine(Root, "Mods");
    public string DisabledMods => Existing(DisabledModsNames);
    public string Tray => Path.Combine(Root, "Tray");

    /// <summary>Выключенные дома и персонажи — вне Tray, игра их не видит.</summary>
    public string DisabledTray => Existing(DisabledTrayNames);

    private string Existing((string Ru, string En) names)
    {
        var ru = Path.Combine(Root, names.Ru);
        var en = Path.Combine(Root, names.En);
        if (Directory.Exists(ru)) return ru;
        if (Directory.Exists(en)) return en;
        return L.IsEnglish ? en : ru;
    }

    /// <summary>Список установленных менеджером участков и семей (какие файлы Tray к чему относятся).</summary>
    public string TrayManifest => Path.Combine(Root, ".modmanager-tray.json");
    public string OptionsIni => Path.Combine(Root, "Options.ini");
    public string Saves => Path.Combine(Root, "saves");
    public string ThumbnailCache => Path.Combine(Root, "localthumbcache.package");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Mods);
        Directory.CreateDirectory(DisabledMods);
    }

    public void EnsureTrayCreated()
    {
        Directory.CreateDirectory(Tray);
        Directory.CreateDirectory(DisabledTray);
    }
}
