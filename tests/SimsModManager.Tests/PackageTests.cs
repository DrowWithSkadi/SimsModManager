using System.Text;
using SimsModManager.Core;
using SimsModManager.Core.Content;
using SimsModManager.Core.Dbpf;

namespace SimsModManager.Tests;

/// <summary>Собирает тестовые ресурсы в том же формате, что и игра.</summary>
internal static class Fake
{
    public static byte[] CasPart(uint bodyType, AgeGender ageGender, string name = "yfBody_Test",
        CasParamFlags flags = CasParamFlags.ShowInUi | CasParamFlags.AllowForCasRandom, uint version = 0x2E,
        params (ushort Cat, uint Val)[] tags)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(version);
        w.Write(0u); // смещение TGI
        w.Write(0);  // пресеты
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(name);
        w.Write7BitEncodedInt(nameBytes.Length);
        w.Write(nameBytes);
        w.Write(1f);
        w.Write((ushort)0);
        w.Write(0u);
        w.Write(0u);
        w.Write((byte)flags);
        if (version >= 39) w.Write((byte)0);
        w.Write(0UL);
        if (version >= 41) w.Write(0UL);
        if (version > 36) w.Write(0UL); else w.Write(0u);
        w.Write(tags.Length);
        foreach (var (cat, val) in tags)
        {
            w.Write(cat);
            if (version >= 37) w.Write(val); else w.Write((ushort)val);
        }
        w.Write(0u); w.Write(0u); w.Write(0u);
        if (version >= 0x2B) w.Write(0u);
        w.Write((byte)0);
        w.Write(bodyType);
        w.Write(0u);
        w.Write((uint)ageGender);
        if (version >= 32) w.Write(1u);
        w.Write(new byte[64]); // остаток ресурса нам не важен
        return ms.ToArray();
    }

    /// <summary>Миниатюра в формате игры: JPEG-заголовок, метка ALFA и маска.</summary>
    public static byte[] Thumbnail()
    {
        var data = new byte[64];
        data[0] = 0xFF; data[1] = 0xD8;
        "ALFA"u8.CopyTo(data.AsSpan(24));
        data[31] = 4; // длина маски (big-endian) = 4
        data[32] = 0x89; data[33] = (byte)'P'; data[34] = (byte)'N'; data[35] = (byte)'G';
        return data;
    }

    public static string Package(string path, params DbpfWriter.Resource[] resources)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        DbpfWriter.Write(fs, resources);
        return path;
    }
}

public class DbpfTests
{
    [Fact]
    public void WritesAndReadsBackResources()
    {
        using var ms = new MemoryStream();
        var key1 = new ResourceKey(0x11111111, 0x22, 0x0123456789ABCDEF);
        var key2 = new ResourceKey(0x33333333, 0, 42);
        DbpfWriter.Write(ms, [new(key1, "hello hello hello"u8.ToArray()), new(key2, [1, 2, 3], Compress: false)]);

        ms.Position = 0;
        using var pkg = DbpfPackage.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal(2, pkg.Entries.Count);
        Assert.Equal(key1, pkg.Entries[0].Key);
        Assert.Equal(DbpfPackage.CompressionZlib, pkg.Entries[0].Compression);
        Assert.Equal("hello hello hello", Encoding.ASCII.GetString(pkg.Read(pkg.Entries[0])));
        Assert.Equal([1, 2, 3], pkg.Read(pkg.Entries[1]));
    }

    [Fact]
    public void RejectsNonPackage()
    {
        Assert.ThrowsAny<InvalidDataException>(() => DbpfPackage.Open(new MemoryStream(new byte[200])));
    }

    [Fact]
    public void DecompressesRefPack()
    {
        // 'abc', затем повтор 6 байт с отступом 3 → «abcabcabc»
        byte[] data = [0x10, 0xFB, 0x00, 0x00, 0x09, 0x0F, 0x02, (byte)'a', (byte)'b', (byte)'c', 0xFC];
        Assert.Equal("abcabcabc", Encoding.ASCII.GetString(RefPack.Decompress(data)));

        // Длинный литерал (4 байта) и финальный байт
        byte[] literal = [0x10, 0xFB, 0x00, 0x00, 0x05, 0xE0, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0xFD, (byte)'e'];
        Assert.Equal("abcde", Encoding.ASCII.GetString(RefPack.Decompress(literal)));
    }
}

public class CasPartTests
{
    [Fact]
    public void ParsesModernCasPart()
    {
        var part = CasPart.Parse(Fake.CasPart(CasPart.BodyFull, AgeGender.Female | AgeGender.Adult | AgeGender.YoungAdult,
            name: "yfBody_Dress", tags: [(1, 100), (2, 200)]));

        Assert.Equal("yfBody_Dress", part.Name);
        Assert.Equal(CasPart.BodyFull, part.BodyType);
        Assert.Equal(ModCategory.Clothing, part.Category);
        Assert.True(part.AgeGender.HasFlag(AgeGender.Female));
        Assert.False(part.AgeGender.HasFlag(AgeGender.Male));
        Assert.Equal(2, part.Tags.Count);
        Assert.Equal(200u, part.Tags[1].Value);
        Assert.True(part.Flags.HasFlag(CasParamFlags.AllowForCasRandom));
    }

    [Fact]
    public void ParsesOlderCasPart()
    {
        var part = CasPart.Parse(Fake.CasPart(CasPart.BodyHair, AgeGender.Male | AgeGender.Adult, version: 0x24, tags: [(5, 7)]));
        Assert.Equal(ModCategory.Hair, part.Category);
        Assert.Equal(7u, part.Tags[0].Value);
    }
}

public class AnalyzerTests
{
    [Fact]
    public void DetectsClothingAndThumbnail()
    {
        using var t = new TestFolder();
        var pkg = Fake.Package(Path.Combine(t.Downloads, "dress.package"),
            new(new ResourceKey(CasPart.ResourceType, 0, 1), Fake.CasPart(CasPart.BodyTop, AgeGender.Female | AgeGender.Adult)),
            new(new ResourceKey(0x3C1AF1F2, 0, 1), Fake.Thumbnail(), Compress: false));

        var (content, thumb) = ModAnalyzer.Analyze([pkg]);

        Assert.Equal(ModCategory.Clothing, content.Category);
        Assert.Equal(1, content.CasPartCount);
        Assert.NotNull(thumb);
        Assert.NotNull(thumb!.AlphaMask);
        Assert.Equal(4, thumb.AlphaMask!.Length);
    }

    [Fact]
    public void DetectsObjectsAndScripts()
    {
        using var t = new TestFolder();
        var obj = Fake.Package(Path.Combine(t.Downloads, "chair.package"),
            new DbpfWriter.Resource(new ResourceKey(0x319E4F1D, 0, 5), new byte[10]));
        Assert.Equal(ModCategory.Objects, ModAnalyzer.Analyze([obj]).Content.Category);

        var script = Path.Combine(t.Downloads, "mod.ts4script");
        File.WriteAllText(script, "x");
        Assert.Equal(ModCategory.Scripts, ModAnalyzer.Analyze([script]).Content.Category);
    }

    [Fact]
    public void BrokenPackageDoesNotCrash()
    {
        using var t = new TestFolder();
        var bad = Path.Combine(t.Downloads, "bad.package");
        File.WriteAllText(bad, "не пакет");
        var (content, thumb) = ModAnalyzer.Analyze([bad]);
        Assert.True(content.Unreadable);
        Assert.Null(thumb);
    }

    [Fact]
    public void CacheRemembersContentAndThumbnail()
    {
        using var t = new TestFolder();
        var folder = Path.Combine(t.Root, "cache");
        var cache = new ContentCache(folder);
        cache.Put("abc", new ModContent { Category = ModCategory.Hair, CasPartCount = 3 }, new ModThumbnail([1, 2], [3]));
        cache.Save();

        var again = new ContentCache(folder);
        Assert.True(again.TryGet("abc", out var c));
        Assert.Equal(ModCategory.Hair, c.Category);
        Assert.Equal([3], again.GetThumbnail("abc")!.AlphaMask!);
    }
}

public class TrashAndDuplicatesTests
{
    [Fact]
    public void DeleteCanBeUndone()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var lib = new ModLibrary(paths);
        Directory.CreateDirectory(Path.Combine(paths.Mods, "Hair"));
        File.WriteAllText(Path.Combine(paths.Mods, "Hair", "h.package"), "x");

        var trashed = lib.MoveToTrash(Assert.Single(lib.Scan()));
        Assert.Empty(lib.Scan());

        lib.UndoDelete(trashed);
        Assert.Single(lib.Scan());

        lib.FinishDelete(lib.MoveToTrash(Assert.Single(lib.Scan())));
        Assert.Empty(lib.Scan());
        Assert.False(Directory.Exists(Path.Combine(paths.Root, ".modmanager-trash")));
    }

    [Fact]
    public void FindsSameFileInTwoMods()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        foreach (var m in new[] { "A", "B" })
        {
            Directory.CreateDirectory(Path.Combine(paths.Mods, m));
            File.WriteAllText(Path.Combine(paths.Mods, m, "same.package"), "123");
        }
        Directory.CreateDirectory(Path.Combine(paths.Mods, "C"));
        File.WriteAllText(Path.Combine(paths.Mods, "C", "other.package"), "1");

        var dups = ModLibrary.FindDuplicates(new ModLibrary(paths).Scan());
        Assert.Equal(2, dups.Count);
    }

    [Fact]
    public void ChangesSingleModOption()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        GameOptions.SetModSettings(paths, mods: null, scripts: false);
        var s = GameOptions.ReadModSettings(paths);
        Assert.True(s.ModsEnabled);
        Assert.False(s.ScriptModsEnabled);
    }
}
