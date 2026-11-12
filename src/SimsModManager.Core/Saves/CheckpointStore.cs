using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimsModManager.Core.Saves;

public enum CheckpointKind
{
    /// <summary>Игрок сам нажал «Сделать точку».</summary>
    Manual,
    /// <summary>Сделана по расписанию или при сохранении игры.</summary>
    Auto,
    /// <summary>Состояние игры прямо перед откатом — чтобы можно было вернуться обратно.</summary>
    BeforeRollback,
}

/// <summary>Точка сохранения: копия одного сохранения игры на определённый момент.</summary>
public sealed class Checkpoint
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    /// <summary>Имя файла сохранения в папке saves, например Slot_00000002.save.</summary>
    public string SlotFile { get; set; } = "";
    /// <summary>Название сохранения в игре (семья/мир), если известно.</summary>
    public string? GameSaveName { get; set; }
    public CheckpointKind Kind { get; set; }
    /// <summary>Закреплённые точки никогда не удаляются автоматически.</summary>
    public bool Pinned { get; set; }
    public long SizeBytes { get; set; }
    public string Hash { get; set; } = "";

    [JsonIgnore] public string Folder { get; set; } = "";
}

/// <summary>Правила автоудаления. null — правило выключено. Закреплённые точки не трогаются никогда.</summary>
public sealed record RetentionRules(int? MaxAgeDays = null, int? MaxCount = null, long? MaxTotalMegabytes = null)
{
    public bool IsEmpty => MaxAgeDays is null && MaxCount is null && MaxTotalMegabytes is null;
}

public sealed class InvalidSaveException(string message) : Exception(message);

/// <summary>
/// Хранилище точек сохранения: %AppData%\SimsModManager\Checkpoints\&lt;id&gt;\ (checkpoint.json + save.zip).
/// Игра их не видит — в папку saves попадает только та точка, к которой откатываются.
/// </summary>
public sealed class CheckpointStore(string folder)
{
    private const string MetaFile = "checkpoint.json";
    private const string DataFile = "save.zip";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimsModManager", "Checkpoints");

    public string Folder { get; } = folder;

    /// <summary>Все точки, самые новые первыми.</summary>
    public IReadOnlyList<Checkpoint> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<Checkpoint>();
        foreach (var dir in Directory.EnumerateDirectories(Folder))
        {
            var meta = Path.Combine(dir, MetaFile);
            if (!File.Exists(meta) || !File.Exists(Path.Combine(dir, DataFile))) continue;
            try
            {
                var checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(meta), JsonOptions);
                if (checkpoint is null) continue;
                checkpoint.Folder = dir;
                result.Add(checkpoint);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Повреждённая запись — пропускаем, остальные точки важнее
            }
        }
        return result.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id, StringComparer.Ordinal).ToList();
    }

    public long TotalBytes() => List().Sum(c => c.SizeBytes);

    /// <summary>
    /// Копирует файл сохранения в новую точку. Если точно такая же копия этого сохранения уже есть
    /// и skipIfUnchanged — возвращает её, не делая дубликат.
    /// </summary>
    public Checkpoint Create(string slotPath, string name, CheckpointKind kind, string? gameSaveName = null,
        DateTime? now = null, bool skipIfUnchanged = false)
    {
        var bytes = ReadShared(slotPath);
        EnsureValidSave(bytes, slotPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var slotFile = Path.GetFileName(slotPath);

        if (skipIfUnchanged)
        {
            var same = List().FirstOrDefault(c =>
                string.Equals(c.SlotFile, slotFile, StringComparison.OrdinalIgnoreCase) && c.Hash == hash);
            if (same is not null) return same;
        }

        var created = now ?? DateTime.Now;
        var id = created.ToString("yyyyMMdd-HHmmss-fff");
        var dir = Path.Combine(Folder, id);
        for (var n = 2; Directory.Exists(dir); n++) dir = Path.Combine(Folder, $"{id}-{n}");
        var temp = dir + ".tmp";
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);

        var zipPath = Path.Combine(temp, DataFile);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(slotFile, CompressionLevel.Fastest);
            entry.LastWriteTime = File.GetLastWriteTime(slotPath);
            using var stream = entry.Open();
            stream.Write(bytes);
        }

        var checkpoint = new Checkpoint
        {
            Id = Path.GetFileName(dir),
            Name = string.IsNullOrWhiteSpace(name) ? DefaultName(kind, created) : name.Trim(),
            CreatedAt = created,
            SlotFile = slotFile,
            GameSaveName = gameSaveName,
            Kind = kind,
            SizeBytes = new FileInfo(zipPath).Length,
            Hash = hash,
        };
        File.WriteAllText(Path.Combine(temp, MetaFile), JsonSerializer.Serialize(checkpoint, JsonOptions));
        // Папка появляется целиком или не появляется вовсе
        Directory.Move(temp, dir);
        checkpoint.Folder = dir;
        return checkpoint;
    }

    public static string DefaultName(CheckpointKind kind, DateTime at) => kind switch
    {
        CheckpointKind.Auto => $"Автосохранение · {at:dd.MM HH:mm}",
        CheckpointKind.BeforeRollback => $"Перед откатом · {at:dd.MM HH:mm}",
        _ => $"Точка · {at:dd.MM HH:mm}",
    };

    /// <summary>
    /// Кладёт точку в папку saves вместо текущего файла того же сохранения.
    /// Пишется во временный файл, проверяется и только потом подменяет — сохранение не может остаться наполовину записанным.
    /// </summary>
    public string Restore(Checkpoint checkpoint, string savesFolder)
    {
        var bytes = ReadData(checkpoint);
        EnsureValidSave(bytes, checkpoint.Name);
        Directory.CreateDirectory(savesFolder);
        var target = Path.Combine(savesFolder, checkpoint.SlotFile);
        var temp = target + ".smm-tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, target, overwrite: true);
        File.SetLastWriteTime(target, DateTime.Now);
        return target;
    }

    public byte[] ReadData(Checkpoint checkpoint)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(checkpoint.Folder, DataFile));
        var entry = zip.Entries.FirstOrDefault() ?? throw new InvalidSaveException($"Точка «{checkpoint.Name}» пустая");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (Convert.ToHexString(SHA256.HashData(bytes)) != checkpoint.Hash)
            throw new InvalidSaveException($"Точка «{checkpoint.Name}» повреждена");
        return bytes;
    }

    public void Delete(Checkpoint checkpoint)
    {
        if (Directory.Exists(checkpoint.Folder)) Directory.Delete(checkpoint.Folder, recursive: true);
    }

    public void Update(Checkpoint checkpoint) =>
        File.WriteAllText(Path.Combine(checkpoint.Folder, MetaFile), JsonSerializer.Serialize(checkpoint, JsonOptions));

    /// <summary>Удаляет старые точки по правилам игрока. Закреплённые не трогает. Возвращает удалённые.</summary>
    public IReadOnlyList<Checkpoint> ApplyRetention(RetentionRules rules, DateTime? now = null)
    {
        if (rules.IsEmpty) return [];
        var all = List();
        var removable = all.Where(c => !c.Pinned).ToList(); // новые первыми
        var doomed = new HashSet<Checkpoint>();
        var at = now ?? DateTime.Now;

        if (rules.MaxAgeDays is int days)
            doomed.UnionWith(removable.Where(c => at - c.CreatedAt > TimeSpan.FromDays(days)));

        if (rules.MaxCount is int max)
            doomed.UnionWith(removable.Where(c => !doomed.Contains(c)).Skip(Math.Max(0, max)));

        if (rules.MaxTotalMegabytes is long mb)
        {
            var limit = mb * 1024 * 1024;
            var total = all.Where(c => !doomed.Contains(c)).Sum(c => c.SizeBytes);
            foreach (var c in removable.Where(c => !doomed.Contains(c)).Reverse())
            {
                if (total <= limit) break;
                doomed.Add(c);
                total -= c.SizeBytes;
            }
        }

        foreach (var c in doomed)
        {
            try { Delete(c); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return doomed.OrderByDescending(c => c.CreatedAt).ToList();
    }

    /// <summary>Сохранения Sims 4 — пакеты DBPF. Всё остальное (пустой или обрезанный файл) не подходит.</summary>
    public static void EnsureValidSave(ReadOnlySpan<byte> bytes, string what)
    {
        if (bytes.Length < 96 || bytes[0] != 'D' || bytes[1] != 'B' || bytes[2] != 'P' || bytes[3] != 'F')
            throw new InvalidSaveException($"«{what}» не похоже на сохранение Sims 4");
    }

    private static byte[] ReadShared(string path)
    {
        // Игра может держать файл открытым — читаем, не мешая ей
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>Файлы сохранений в папке saves.</summary>
public static class SaveSlots
{
    public static string FileName(long slotId) => $"Slot_{slotId:x8}.save";

    /// <summary>Самое недавно записанное сохранение (без резервных .verN).</summary>
    public static string? MostRecent(string savesFolder) =>
        Directory.Exists(savesFolder)
            ? Directory.GetFiles(savesFolder, "Slot_*.save")
                .Where(f => f.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
}
