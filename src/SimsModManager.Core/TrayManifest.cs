using System.Text.Json;

namespace SimsModManager.Core;

/// <summary>
/// Запись о скачанном участке, комнате или семье. В папке Tray файлы лежат вперемешку
/// (с именами вида 0x00000002!0x0a1b2c….trayitem), поэтому запоминаем, какие файлы от какой загрузки.
/// </summary>
public sealed class TrayEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public TrayKind Kind { get; set; }
    public List<string> Files { get; set; } = [];
    public DateTime InstalledAt { get; set; } = DateTime.Now;
    public bool Enabled { get; set; } = true;
}

public sealed class TrayManifest
{
    public List<TrayEntry> Entries { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static TrayManifest Load(SimsPaths paths)
    {
        try
        {
            if (File.Exists(paths.TrayManifest))
                return JsonSerializer.Deserialize<TrayManifest>(File.ReadAllText(paths.TrayManifest), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save(SimsPaths paths)
    {
        var tmp = paths.TrayManifest + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, paths.TrayManifest, overwrite: true);
    }
}
