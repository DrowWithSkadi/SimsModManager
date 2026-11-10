namespace SimsModManager.Core;

/// <summary>Пути внутри пользовательской папки игры.</summary>
public sealed class SimsPaths(string simsFolder)
{
    /// <summary>Название папки, куда мы убираем выключенные моды. Она лежит вне Mods, поэтому игра её не видит.</summary>
    public const string DisabledFolderName = "Mods (выключенные)";

    public string Root { get; } = Path.GetFullPath(simsFolder);
    public string Mods => Path.Combine(Root, "Mods");
    public string DisabledMods => Path.Combine(Root, DisabledFolderName);
    public string Tray => Path.Combine(Root, "Tray");

    /// <summary>Выключенные дома и персонажи — вне Tray, игра их не видит.</summary>
    public string DisabledTray => Path.Combine(Root, "Tray (выключенные)");

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
