using System.Buffers.Binary;
using SimsModManager.Core.Dbpf;

namespace SimsModManager.Core.Content;

/// <summary>Тип мода. Новые значения добавляются только в конец — номера хранятся в кеше.</summary>
public enum ModCategory { Unknown, Clothing, Hair, Makeup, Objects, Scripts, Other, Tray }

/// <summary>Что лежит внутри мода. Считается один раз и кешируется.</summary>
public sealed record ModContent
{
    public ModCategory Category { get; init; } = ModCategory.Unknown;
    public int CasPartCount { get; init; }
    public int ObjectCount { get; init; }
    public bool HasThumbnail { get; init; }
    /// <summary>Название первой вещи из файла (например, «yfBody_Dress»), если есть.</summary>
    public string? FirstPartName { get; init; }
    public bool Unreadable { get; init; }
}

/// <summary>Картинка-миниатюра из .package: JPEG/PNG и, возможно, отдельная маска прозрачности.</summary>
public sealed record ModThumbnail(byte[] Image, byte[]? AlphaMask);

/// <summary>Разбирает файлы мода: что это (одежда, причёска, объект…) и какая у него картинка.</summary>
public static class ModAnalyzer
{
    /// <summary>Типы ресурсов-миниатюр. Первым — миниатюры одежды, потом объектов.</summary>
    private static readonly uint[] ThumbnailTypes =
        [0x3C1AF1F2, 0x5B282D45, 0x3C2A8647, 0xCD9DE247, 0x9C925813, 0x0D338A3A];

    private const uint ObjectDefinition = 0xC0DB5AE7;
    private const uint ObjectCatalog = 0x319E4F1D;

    public static (ModContent Content, ModThumbnail? Thumbnail) Analyze(IReadOnlyList<string> files)
    {
        // Участок, комната или семья для папки Tray
        if (files.Count > 0 && files.All(ModFiles.IsTrayFile))
        {
            var trayThumb = FindTrayThumbnail(files);
            return (new ModContent { Category = ModCategory.Tray, HasThumbnail = trayThumb is not null }, trayThumb);
        }

        var packages = files.Where(ModFiles.IsPackage).ToList();
        var hasScripts = files.Any(ModFiles.IsScript);

        int casCount = 0, objectCount = 0;
        var categories = new Dictionary<ModCategory, int>();
        ModThumbnail? thumb = null;
        string? firstName = null;
        var unreadable = false;

        foreach (var file in packages)
        {
            try
            {
                using var pkg = DbpfPackage.Open(file);
                var live = pkg.Entries.Where(e => !e.IsDeleted).ToList();

                var casEntries = live.Where(e => e.Key.Type == CasPart.ResourceType).ToList();
                casCount += casEntries.Count;
                objectCount += live.Count(e => e.Key.Type is ObjectDefinition or ObjectCatalog);

                // Тип мода определяем по первым нескольким вещам — читать все сотни не нужно.
                foreach (var entry in casEntries.Take(8))
                {
                    try
                    {
                        var part = CasPart.Parse(pkg.Read(entry));
                        firstName ??= part.Name;
                        categories[part.Category] = categories.GetValueOrDefault(part.Category) + 1;
                    }
                    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
                }

                thumb ??= FindThumbnail(pkg, live);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException)
            {
                unreadable = true;
            }
        }

        ModCategory category;
        if (categories.Count > 0) category = categories.MaxBy(kv => kv.Value).Key;
        else if (objectCount > 0) category = ModCategory.Objects;
        else if (hasScripts && packages.Count == 0) category = ModCategory.Scripts;
        else if (hasScripts) category = ModCategory.Other;
        else category = packages.Count > 0 ? ModCategory.Other : ModCategory.Unknown;

        return (new ModContent
        {
            Category = category,
            CasPartCount = casCount,
            ObjectCount = objectCount,
            HasThumbnail = thumb is not null,
            FirstPartName = firstName,
            Unreadable = unreadable && casCount == 0 && objectCount == 0,
        }, thumb);
    }

    private static ModThumbnail? FindThumbnail(DbpfPackage pkg, List<ResourceEntry> entries)
    {
        foreach (var type in ThumbnailTypes)
        {
            // Берём самую крупную миниатюру этого типа — она обычно лучше выглядит.
            foreach (var entry in entries.Where(e => e.Key.Type == type).OrderByDescending(e => e.MemorySize).Take(3))
            {
                try
                {
                    var data = pkg.Read(entry);
                    var thumb = ParseThumbnail(data);
                    if (thumb is not null) return thumb;
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
            }
        }
        return null;
    }

    /// <summary>
    /// Превью участка, комнаты или персонажа: внутри .bpi/.rmi/.hhi/.sgi после небольшого
    /// служебного заголовка лежит обычная картинка JPEG или PNG — ищем её начало.
    /// </summary>
    private static ModThumbnail? FindTrayThumbnail(IReadOnlyList<string> files)
    {
        string[] imageExts = [".bpi", ".rmi", ".hhi", ".sgi"];
        var candidates = files
            .Where(f => imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Array.IndexOf(imageExts, Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in candidates)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 16 * 1024 * 1024) continue;
                var data = File.ReadAllBytes(file);
                var start = FindImageStart(data);
                if (start >= 0) return new ModThumbnail(data[start..], null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    internal static int FindImageStart(byte[] data)
    {
        for (var i = 0; i + 8 < data.Length && i < 4096; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF) return i;
            if (data[i] == 0x89 && data[i + 1] == 'P' && data[i + 2] == 'N' && data[i + 3] == 'G') return i;
        }
        return -1;
    }

    /// <summary>
    /// Миниатюры одежды — это JPEG, в который игра вписывает маску прозрачности:
    /// после 24 байт идёт метка «ALFA», длина (big-endian) и PNG-маска.
    /// </summary>
    public static ModThumbnail? ParseThumbnail(byte[] data)
    {
        if (data.Length < 8) return null;
        var isJpeg = data[0] == 0xFF && data[1] == 0xD8;
        var isPng = data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G';
        if (!isJpeg && !isPng) return null;

        byte[]? alpha = null;
        if (isJpeg && data.Length > 32 && data[24] == 'A' && data[25] == 'L' && data[26] == 'F' && data[27] == 'A')
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(28));
            if (len > 0 && 32 + len <= data.Length)
                alpha = data.AsSpan(32, len).ToArray();
        }
        return new ModThumbnail(data, alpha);
    }
}
