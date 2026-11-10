using System.Text.Json;

namespace SimsModManager.Core.Health;

/// <summary>
/// «Найти виновника» методом 50/50: выключаем половину подозреваемых модов, игрок проверяет игру,
/// отвечает «проблема есть / пропала» — и круг сужается вдвое. Для 1000 модов хватает 10 проверок.
/// Состояние хранится в файле, чтобы его не потерять, если программу закроют посреди поиска.
/// </summary>
public sealed class ModBisect
{
    /// <summary>Моды, которые были включены до начала поиска (по именам) — их вернём в конце.</summary>
    public List<string> OriginallyEnabled { get; set; } = [];

    /// <summary>Под подозрением сейчас.</summary>
    public List<string> Suspects { get; set; } = [];

    /// <summary>Какая половина подозреваемых сейчас включена для проверки.</summary>
    public List<string> TestingNow { get; set; } = [];

    public int Step { get; set; }

    /// <summary>Когда игру запустили для текущей проверки — после её закрытия менеджер сам посмотрит отчёты об ошибках.</summary>
    public DateTime? LaunchedAt { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;

    public bool IsFinished => Suspects.Count <= 1;
    public string? Culprit => Suspects.Count == 1 ? Suspects[0] : null;

    /// <summary>Сколько ещё примерно проверок понадобится.</summary>
    public int StepsLeft => Suspects.Count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(Suspects.Count));

    public static ModBisect Start(IEnumerable<string> enabledMods, IEnumerable<string>? suspects = null)
    {
        var enabled = enabledMods.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var bisect = new ModBisect
        {
            OriginallyEnabled = enabled,
            Suspects = (suspects ?? enabled).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        bisect.NextHalf();
        return bisect;
    }

    private void NextHalf()
    {
        Step++;
        TestingNow = Suspects.Take((Suspects.Count + 1) / 2).ToList();
    }

    /// <summary>Какие моды должны быть включены для текущей проверки: невиновные + проверяемая половина.</summary>
    public IReadOnlySet<string> ShouldBeEnabled()
    {
        var set = new HashSet<string>(OriginallyEnabled, StringComparer.OrdinalIgnoreCase);
        set.ExceptWith(Suspects.Except(TestingNow, StringComparer.OrdinalIgnoreCase));
        return set;
    }

    /// <summary>Ответ игрока после проверки.</summary>
    public void Answer(bool problemStillThere)
    {
        if (IsFinished) return;
        Suspects = problemStillThere
            ? TestingNow
            : Suspects.Except(TestingNow, StringComparer.OrdinalIgnoreCase).ToList();
        if (!IsFinished) NextHalf();
        else TestingNow = [];
    }

    // ---------- Сохранение ----------

    private static string FileOf(SimsPaths paths) => Path.Combine(paths.Root, ".modmanager-bisect.json");

    public void Save(SimsPaths paths) =>
        File.WriteAllText(FileOf(paths), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static ModBisect? Load(SimsPaths paths)
    {
        try
        {
            var file = FileOf(paths);
            return File.Exists(file) ? JsonSerializer.Deserialize<ModBisect>(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Clear(SimsPaths paths)
    {
        try { File.Delete(FileOf(paths)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
