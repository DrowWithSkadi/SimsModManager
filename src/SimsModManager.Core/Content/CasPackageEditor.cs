using SimsModManager.Core.Dbpf;

namespace SimsModManager.Core.Content;

/// <summary>Одна расцветка вещи в файле .package.</summary>
public sealed record CasSwatch(string PackagePath, ResourceEntry Entry, CasPart Part)
{
    public CasSettings Settings => CasSettings.From(Part);
}

/// <summary>Вещь: набор расцветок одного типа из одного файла.</summary>
public sealed record CasItem(string PackagePath, uint BodyType, string BodyTypeName, IReadOnlyList<CasSwatch> Swatches)
{
    public ModCategory Category => Swatches[0].Part.Category;
}

/// <summary>Чтение и сохранение настроек вещей внутри .package.</summary>
public static class CasPackageEditor
{
    /// <summary>Все вещи CAS в файлах мода (одежда, причёски, макияж).</summary>
    public static IReadOnlyList<CasItem> ReadItems(IEnumerable<string> files)
    {
        var items = new List<CasItem>();
        foreach (var file in files.Where(ModFiles.IsPackage))
        {
            try
            {
                using var pkg = DbpfPackage.Open(file);
                var swatches = new List<CasSwatch>();
                foreach (var entry in pkg.Entries.Where(e => e.Key.Type == CasPart.ResourceType && !e.IsDeleted))
                {
                    try { swatches.Add(new CasSwatch(file, entry, CasPart.Parse(pkg.Read(entry)))); }
                    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
                }
                // Расцветки группируем по типу части тела: обычно в файле одна вещь в нескольких цветах.
                foreach (var group in swatches.GroupBy(s => s.Part.BodyType))
                {
                    var list = group.ToList();
                    items.Add(new CasItem(file, group.Key, list[0].Part.BodyTypeName, list));
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException) { }
        }
        return items;
    }

    private const uint CasThumbnailType = 0x3C1AF1F2;

    /// <summary>
    /// Миниатюры нескольких расцветок за одно открытие файла — важно для сборников на сотни вещей.
    /// Ключ — номер CASP; у миниатюры расцветки тот же номер.
    /// </summary>
    public static IReadOnlyDictionary<ulong, ModThumbnail> ReadSwatchThumbnails(string packagePath, IEnumerable<ulong> instances)
    {
        var result = new Dictionary<ulong, ModThumbnail>();
        try
        {
            using var pkg = DbpfPackage.Open(packagePath);
            var thumbs = pkg.Entries.Where(e => e.Key.Type == CasThumbnailType && !e.IsDeleted)
                .GroupBy(e => e.Key.Instance)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.MemorySize).First());
            foreach (var instance in instances.Distinct())
            {
                if (!thumbs.TryGetValue(instance, out var entry)) continue;
                try
                {
                    if (ModAnalyzer.ParseThumbnail(pkg.Read(entry)) is { } t) result[instance] = t;
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException) { }
        return result;
    }

    /// <summary>
    /// Автор по названию: «[Автор] Платье» или первая часть внутреннего имени «автор_yfTop_…».
    /// Внутренние имена игры начинаются с кода возраста и пола (yf, am, cu…) — их пропускаем.
    /// </summary>
    public static string? GuessAuthor(string modName, IEnumerable<string> partNames)
    {
        var bracket = System.Text.RegularExpressions.Regex.Match(modName, @"^\s*[\[(]([^\])]{2,40})[\])]");
        if (bracket.Success) return bracket.Groups[1].Value.Trim();

        foreach (var name in partNames)
        {
            var first = name.Split('_', 2)[0];
            if (first.Length < 3 || first.Length > 30) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(first, "^[ytacpe][fmu][A-Z]")) continue;
            if (!first.Any(char.IsLetter)) continue;
            return first;
        }
        return null;
    }

    /// <summary>Миниатюра конкретной расцветки (у неё тот же номер, что у CASP).</summary>
    public static ModThumbnail? ReadSwatchThumbnail(CasSwatch swatch)
    {
        try
        {
            using var pkg = DbpfPackage.Open(swatch.PackagePath);
            var entry = pkg.Entries.FirstOrDefault(e => e.Key.Type == 0x3C1AF1F2 && e.Key.Instance == swatch.Entry.Key.Instance)
                        ?? pkg.Entries.FirstOrDefault(e => e.Key.Type == 0x3C1AF1F2);
            return entry is null ? null : ModAnalyzer.ParseThumbnail(pkg.Read(entry));
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Записывает новые настройки в файл. Остальные ресурсы переносятся байт в байт.
    /// Файл заменяется целиком только после успешной записи во временный.
    /// </summary>
    public static int Save(string packagePath, IReadOnlyDictionary<ResourceKey, CasSettings> changes)
    {
        var tmp = packagePath + ".smm-tmp";
        var changed = 0;
        try
        {
            using (var pkg = DbpfPackage.Open(packagePath))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                var resources = new List<DbpfWriter.Resource>();
                foreach (var entry in pkg.Entries.Where(e => !e.IsDeleted))
                {
                    if (entry.Key.Type == CasPart.ResourceType && changes.TryGetValue(entry.Key, out var settings))
                    {
                        var data = pkg.Read(entry);
                        var current = CasSettings.From(CasPart.Parse(data));
                        if (!current.SameAs(settings))
                        {
                            resources.Add(new DbpfWriter.Resource(entry.Key, CasPartWriter.Apply(data, settings)));
                            changed++;
                            continue;
                        }
                    }
                    resources.Add(new DbpfWriter.Resource(entry.Key, pkg.ReadRaw(entry))
                    {
                        StoredCompression = entry.Compression,
                        MemorySize = entry.MemorySize,
                    });
                }
                DbpfWriter.Write(output, resources);
            }

            if (changed == 0)
            {
                File.Delete(tmp);
                return 0;
            }
            File.Move(tmp, packagePath, overwrite: true);
            return changed;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}

/// <summary>
/// Копии оригинальных файлов до первого изменения. Хранятся в %LocalAppData%\Ancora\originals,
/// ключ — «название мода / имя файла», поэтому переживают включение и выключение мода.
/// </summary>
public sealed class OriginalsStore(string? folder = null)
{
    private readonly string _folder = folder ?? AppIdentity.Local("originals");

    private string PathFor(string modName, string fileName) =>
        Path.Combine(_folder, ModFiles.SanitizeName(modName), fileName);

    /// <summary>Сохраняет копию, если её ещё нет (то есть только самый первый, авторский вариант).</summary>
    public void EnsureBackup(string modName, string filePath)
    {
        var backup = PathFor(modName, Path.GetFileName(filePath));
        if (File.Exists(backup)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(filePath, backup);
    }

    public bool HasBackup(string modName) =>
        Directory.Exists(Path.Combine(_folder, ModFiles.SanitizeName(modName)))
        && Directory.EnumerateFiles(Path.Combine(_folder, ModFiles.SanitizeName(modName))).Any();

    /// <summary>Возвращает авторские файлы на место текущих и удаляет копии.</summary>
    public int Restore(string modName, IEnumerable<string> currentFiles)
    {
        var restored = 0;
        foreach (var file in currentFiles)
        {
            var backup = PathFor(modName, Path.GetFileName(file));
            if (!File.Exists(backup)) continue;
            File.Copy(backup, file, overwrite: true);
            File.Delete(backup);
            restored++;
        }
        Forget(modName);
        return restored;
    }

    /// <summary>Мод удалили или переустановили — старые копии больше не нужны.</summary>
    /// <summary>Мод переименовали — копии оригинала переезжают под новое имя.</summary>
    public void Rename(string oldName, string newName)
    {
        var from = Path.Combine(_folder, ModFiles.SanitizeName(oldName));
        var to = Path.Combine(_folder, ModFiles.SanitizeName(newName));
        try
        {
            if (Directory.Exists(from) && !Directory.Exists(to)) Directory.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Forget(string modName)
    {
        var dir = Path.Combine(_folder, ModFiles.SanitizeName(modName));
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
