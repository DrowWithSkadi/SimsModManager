using System.IO.Compression;

namespace SimsModManager.Tests;

/// <summary>Временная папка «Документы» с фейковой установкой Sims 4.</summary>
public sealed class TestFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "smm-tests", Guid.NewGuid().ToString("N"));
    public string Documents => Path.Combine(Root, "Documents");
    public string Sims => Path.Combine(Documents, "Electronic Arts", "The Sims 4");
    public string Downloads => Path.Combine(Root, "Downloads");

    public TestFolder()
    {
        Directory.CreateDirectory(Path.Combine(Sims, "Mods"));
        Directory.CreateDirectory(Downloads);
        File.WriteAllText(Path.Combine(Sims, "Options.ini"), "[options]\r\nmodsdisabled = 0\r\nscriptmodsenabled = 1\r\n");
    }

    /// <summary>Создаёт zip-архив в «Загрузках». Ключ — путь внутри архива, значение — содержимое.</summary>
    public string MakeZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(Downloads, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
    }
}
