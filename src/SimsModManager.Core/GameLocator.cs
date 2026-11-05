namespace SimsModManager.Core;

/// <summary>
/// Ищет пользовательскую папку The Sims 4 (там, где лежат Mods, saves, Options.ini).
/// Обычно это «Документы\Electronic Arts\The Sims 4», но папка «Документы» может быть
/// перенесена в OneDrive, а сама папка игры на некоторых языках называется иначе
/// (например, «Die Sims 4» или «Les Sims 4»).
/// </summary>
public static class GameLocator
{
    private static readonly string[] Markers = ["Options.ini", "GameVersion.txt", "Mods", "saves", "Tray"];

    /// <summary>Возвращает найденные папки игры, лучшая — первой.</summary>
    public static IReadOnlyList<string> FindSimsFolders(IEnumerable<string>? documentRoots = null)
    {
        var roots = (documentRoots ?? GetDocumentRoots())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var found = new List<(string Path, int Score)>();
        foreach (var docs in roots)
        {
            var ea = Path.Combine(docs, "Electronic Arts");
            if (!Directory.Exists(ea)) continue;

            IEnumerable<string> subDirs;
            try { subDirs = Directory.GetDirectories(ea); }
            catch (Exception) { continue; }

            foreach (var dir in subDirs)
            {
                var score = Score(dir);
                if (score > 0) found.Add((Path.GetFullPath(dir), score));
            }
        }

        return found
            .OrderByDescending(f => f.Score)
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? FindSimsFolder(IEnumerable<string>? documentRoots = null) =>
        FindSimsFolders(documentRoots).FirstOrDefault();

    /// <summary>Похожа ли папка на пользовательскую папку Sims 4.</summary>
    public static bool LooksLikeSimsFolder(string dir) => Directory.Exists(dir) && Score(dir) > 0;

    private static int Score(string dir)
    {
        var name = Path.GetFileName(dir);
        var nameMatches = name.Contains("Sims 4", StringComparison.OrdinalIgnoreCase)
                          || name.Contains("Sims4", StringComparison.OrdinalIgnoreCase);

        var markerCount = Markers.Count(m =>
            File.Exists(Path.Combine(dir, m)) || Directory.Exists(Path.Combine(dir, m)));

        // Папка с подходящим именем, но без признаков игры (например, пустая) — слабый кандидат.
        if (markerCount == 0) return nameMatches ? 1 : 0;
        return markerCount * 10 + (nameMatches ? 5 : 0);
    }

    public sealed record Candidate(string Label, string Path, bool Exists, bool LooksValid);

    /// <summary>
    /// Места, где обычно лежит папка игры, — для экрана «папка не найдена».
    /// Показываем только реально существующие «Документы».
    /// </summary>
    public static IReadOnlyList<Candidate> GetCandidates()
    {
        var result = new List<Candidate>();
        foreach (var docs in GetDocumentRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var label = docs.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
                ? "Документы OneDrive"
                : "Документы на этом компьютере";
            var ea = System.IO.Path.Combine(docs, "Electronic Arts");
            var found = false;
            if (Directory.Exists(ea))
            {
                foreach (var dir in SafeDirs(ea))
                {
                    found = true;
                    result.Add(new Candidate(label, System.IO.Path.GetFullPath(dir), true, Score(dir) > 1));
                }
            }
            if (!found)
                result.Add(new Candidate(label, System.IO.Path.Combine(ea, "The Sims 4"), false, false));
        }
        return result
            .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .ToList();
    }

    private static string[] SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception) { return []; }
    }

    /// <summary>Все места, где может оказаться папка «Документы».</summary>
    private static IEnumerable<string> GetDocumentRoots()
    {
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(myDocs)) yield return myDocs;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) yield break;

        string[] docNames = ["Documents", "Документы", "Dokumente", "Documentos", "Mes documents", "Dokumenty", "Dokumendid"];
        foreach (var n in docNames) yield return Path.Combine(profile, n);

        // OneDrive может называться «OneDrive» или «OneDrive - Организация»
        string[] oneDrives;
        try { oneDrives = Directory.GetDirectories(profile, "OneDrive*"); }
        catch (Exception) { oneDrives = []; }

        foreach (var od in oneDrives)
            foreach (var n in docNames)
                yield return Path.Combine(od, n);
    }
}
