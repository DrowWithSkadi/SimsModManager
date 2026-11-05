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
/// Копии оригинальных файлов до первого изменения. Хранятся в %LocalAppData%\SimsModManager\originals,
/// ключ — «название мода / имя файла», поэтому переживают включение и выключение мода.
/// </summary>
public sealed class OriginalsStore(string? folder = null)
{
    private readonly string _folder = folder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimsModManager", "originals");

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
    public void Forget(string modName)
    {
        var dir = Path.Combine(_folder, ModFiles.SanitizeName(modName));
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
