using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimsModManager.Core.Content;

/// <summary>
/// Кеш разбора модов: чтобы при каждом запуске не читать заново тысячи файлов.
/// Запись становится недействительной, как только меняются размер или дата файлов мода.
/// </summary>
public sealed class ContentCache
{
    private sealed class Entry
    {
        public string Stamp { get; set; } = "";
        public ModContent Content { get; set; } = new();
    }

    private readonly string _folder;
    private readonly Dictionary<string, Entry> _entries;
    private readonly Lock _lock = new();
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ContentCache(string? folder = null)
    {
        _folder = folder ?? AppIdentity.Local("cache");
        _entries = Load();
    }

    private string IndexPath => Path.Combine(_folder, "content.json");
    private string ThumbFolder => Path.Combine(_folder, "thumbs");

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(IndexPath))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(IndexPath), JsonOptions)
                       ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>«Отпечаток» файлов мода: если он совпадает — разбирать заново не нужно.</summary>
    public static string StampOf(IReadOnlyList<string> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(f);
            if (!info.Exists) continue;
            sb.Append("v3|").Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Ключ записи — содержимое мода, а не путь: при включении/выключении мода кеш не теряется.</summary>
    public bool TryGet(string stamp, out ModContent content)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(stamp, out var e))
            {
                content = e.Content;
                return true;
            }
        }
        content = new ModContent();
        return false;
    }

    public void Put(string stamp, ModContent content, ModThumbnail? thumb)
    {
        lock (_lock)
        {
            _entries[stamp] = new Entry { Stamp = stamp, Content = content };
            _dirty = true;
        }
        if (thumb is null) return;
        try
        {
            Directory.CreateDirectory(ThumbFolder);
            File.WriteAllBytes(Path.Combine(ThumbFolder, stamp + ".img"), thumb.Image);
            if (thumb.AlphaMask is not null)
                File.WriteAllBytes(Path.Combine(ThumbFolder, stamp + ".alpha"), thumb.AlphaMask);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public ModThumbnail? GetThumbnail(string stamp)
    {
        try
        {
            var img = Path.Combine(ThumbFolder, stamp + ".img");
            if (!File.Exists(img)) return null;
            var alpha = Path.Combine(ThumbFolder, stamp + ".alpha");
            return new ModThumbnail(File.ReadAllBytes(img), File.Exists(alpha) ? File.ReadAllBytes(alpha) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            json = JsonSerializer.Serialize(_entries, JsonOptions);
            _dirty = false;
        }
        try
        {
            Directory.CreateDirectory(_folder);
            var tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
