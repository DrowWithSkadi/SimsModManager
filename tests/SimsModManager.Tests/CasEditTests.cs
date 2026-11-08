using SimsModManager.Core;
using System.Buffers.Binary;
using SimsModManager.Core.Content;
using SimsModManager.Core.Dbpf;

namespace SimsModManager.Tests;

public class CasEditTests
{
    private static readonly AgeGender Adults = AgeGender.YoungAdult | AgeGender.Adult;

    [Fact]
    public void WriterChangesGenderAgesOutfitsAndRandom()
    {
        var data = Fake.CasPart(CasPart.BodyFull, AgeGender.Female | AgeGender.Male | AgeGender.Teen | Adults,
            flags: CasParamFlags.ShowInUi | CasParamFlags.AllowForCasRandom | CasParamFlags.AllowForLiveRandom,
            tags: [(OutfitTags.TagCategory, OutfitTags.Everyday), (5, 999), (OutfitTags.TagCategory, 2000)]);

        var edited = CasPartWriter.Apply(data,
            new CasSettings(Adults, Female: true, Male: false, new HashSet<uint> { OutfitTags.Formal, OutfitTags.Party }, AllowRandom: false));
        var part = CasPart.Parse(edited);
        var s = CasSettings.From(part);

        Assert.True(s.Female);
        Assert.False(s.Male);
        Assert.Equal(Adults, s.Ages);
        Assert.False(s.AllowRandom);
        Assert.True(s.Outfits.SetEquals([OutfitTags.Formal, OutfitTags.Party]));
        Assert.True(part.Flags.HasFlag(CasParamFlags.RestrictOppositeGender));
        Assert.True(part.Flags2.HasFlag(CasParamFlags2.RestrictOppositeFrame));
        Assert.True(part.Flags.HasFlag(CasParamFlags.ShowInUi)); // чужие флаги не тронуты
        // Посторонние метки и неизвестные значения категории одежды сохраняются
        Assert.Contains(part.Tags, t => t.Category == 5 && t.Value == 999);
        Assert.Contains(part.Tags, t => t.Category == OutfitTags.TagCategory && t.Value == 2000);
        Assert.Equal(CasPart.BodyFull, part.BodyType);
        Assert.Equal("yfBody_Test", part.Name);
    }

    [Fact]
    public void WriterChangesWeatherAndSeasons()
    {
        var data = Fake.CasPart(CasPart.BodyTop, AgeGender.Female | Adults,
            tags: [(OutfitTags.TagCategory, OutfitTags.Everyday), (OutfitTags.TagCategory, OutfitTags.ColdWeather),
                   (SeasonTags.TagCategory, SeasonTags.Winter), (SeasonTags.TagCategory, 1234)]);

        var before = CasSettings.From(CasPart.Parse(data));
        Assert.True(before.Outfits.SetEquals([OutfitTags.Everyday, OutfitTags.ColdWeather]));
        Assert.True(before.Seasons!.SetEquals([SeasonTags.Winter]));

        var edited = CasPartWriter.Apply(data, before with
        {
            Outfits = new HashSet<uint> { OutfitTags.Everyday, OutfitTags.HotWeather },
            Seasons = new HashSet<uint> { SeasonTags.Spring, SeasonTags.Summer },
        });
        var part = CasPart.Parse(edited);
        var after = CasSettings.From(part);

        Assert.True(after.Outfits.SetEquals([OutfitTags.Everyday, OutfitTags.HotWeather]));
        Assert.True(after.Seasons!.SetEquals([SeasonTags.Spring, SeasonTags.Summer]));
        Assert.Contains(part.Tags, t => t.Category == SeasonTags.TagCategory && t.Value == 1234); // чужая метка стиля цела
    }

    [Fact]
    public void SeasonsKeptWhenNotGiven()
    {
        var data = Fake.CasPart(CasPart.BodyTop, AgeGender.Female | Adults,
            tags: [(OutfitTags.TagCategory, OutfitTags.Everyday), (SeasonTags.TagCategory, SeasonTags.Fall)]);

        var edited = CasPartWriter.Apply(data, new CasSettings(Adults, true, false, new HashSet<uint> { OutfitTags.Formal }, false));

        Assert.True(CasSettings.From(CasPart.Parse(edited)).Seasons!.SetEquals([SeasonTags.Fall]));
    }

    [Fact]
    public void SmallBusinessTagIsNotSwimwear()
    {
        // 79 — OutfitCategory_SmallBusiness; раньше менеджер ошибочно считал её купальной
        var data = Fake.CasPart(CasPart.BodyFull, AgeGender.Female | Adults,
            tags: [(OutfitTags.TagCategory, 79), (OutfitTags.TagCategory, OutfitTags.Swimwear)]);

        var s = CasSettings.From(CasPart.Parse(data));
        Assert.True(s.Outfits.SetEquals([OutfitTags.Swimwear]));

        var edited = CasPart.Parse(CasPartWriter.Apply(data, s with { Outfits = new HashSet<uint>() }));
        Assert.Contains(edited.Tags, t => t.Category == OutfitTags.TagCategory && t.Value == 79);
        Assert.DoesNotContain(edited.Tags, t => t.Value == OutfitTags.Swimwear);
    }

    [Fact]
    public void TgiOffsetShiftsWithTagCount()
    {
        var data = Fake.CasPart(CasPart.BodyTop, AgeGender.Female | Adults, tags: [(1, 1)]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(data.Length - 8 - 16)); // «ссылки» в конце

        var edited = CasPartWriter.Apply(data, new CasSettings(Adults, true, false,
            new HashSet<uint> { OutfitTags.Everyday, OutfitTags.Formal, OutfitTags.Sleep }, false));

        var oldOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var newOffset = BinaryPrimitives.ReadUInt32LittleEndian(edited.AsSpan(4));
        Assert.Equal(edited.Length - data.Length, (int)(newOffset - oldOffset));
        Assert.Equal(18, edited.Length - data.Length); // 3 метки по 6 байт
    }

    [Fact]
    public void PackageSaveKeepsOtherResourcesIdentical()
    {
        using var t = new TestFolder();
        var other = new byte[300];
        new Random(1).NextBytes(other);
        var path = Fake.Package(Path.Combine(t.Downloads, "dress.package"),
            new(new ResourceKey(CasPart.ResourceType, 0, 1), Fake.CasPart(CasPart.BodyFull, AgeGender.Female | AgeGender.Male | Adults)),
            new(new ResourceKey(CasPart.ResourceType, 0, 2), Fake.CasPart(CasPart.BodyFull, AgeGender.Female | AgeGender.Male | Adults)),
            new(new ResourceKey(0x12345678, 0, 3), other));

        var item = Assert.Single(CasPackageEditor.ReadItems([path]));
        Assert.Equal(2, item.Swatches.Count);

        var womenOnly = new CasSettings(Adults, true, false, new HashSet<uint> { OutfitTags.Everyday }, false);
        var changed = CasPackageEditor.Save(path, item.Swatches.ToDictionary(s => s.Entry.Key, _ => womenOnly));
        Assert.Equal(2, changed);

        using var pkg = DbpfPackage.Open(path);
        Assert.Equal(3, pkg.Entries.Count);
        Assert.Equal(other, pkg.Read(pkg.Entries.Single(e => e.Key.Type == 0x12345678)));
        foreach (var swatch in CasPackageEditor.ReadItems([path])[0].Swatches)
            Assert.True(swatch.Settings.SameAs(womenOnly));

        // Повторное сохранение тех же настроек файл не трогает
        Assert.Equal(0, CasPackageEditor.Save(path, item.Swatches.ToDictionary(s => s.Entry.Key, _ => womenOnly)));
    }

    [Fact]
    public void OriginalCanBeRestored()
    {
        using var t = new TestFolder();
        var store = new OriginalsStore(Path.Combine(t.Root, "originals"));
        var file = Path.Combine(t.Downloads, "a.package");
        File.WriteAllText(file, "author");

        store.EnsureBackup("Мод", file);
        File.WriteAllText(file, "edited once");
        store.EnsureBackup("Мод", file); // вторая правка не перезаписывает авторскую копию
        File.WriteAllText(file, "edited twice");

        Assert.True(store.HasBackup("Мод"));
        Assert.Equal(1, store.Restore("Мод", [file]));
        Assert.Equal("author", File.ReadAllText(file));
        Assert.False(store.HasBackup("Мод"));
    }
}

public class RenameAndAuthorTests
{
    [Fact]
    public void RenamesFolderModAndKeepsFiles()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var lib = new ModLibrary(paths);
        Directory.CreateDirectory(Path.Combine(paths.Mods, "dl_8f3a2"));
        File.WriteAllText(Path.Combine(paths.Mods, "dl_8f3a2", "x.package"), "x");

        var renamed = lib.Rename(Assert.Single(lib.Scan()), "Льняное платье");

        Assert.Equal("Льняное платье", renamed.Name);
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Льняное платье", "x.package")));
        Assert.Equal("Льняное платье", Assert.Single(lib.Scan()).Name);
    }

    [Fact]
    public void RenameRefusesExistingName()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var lib = new ModLibrary(paths);
        foreach (var n in new[] { "A", "B" })
        {
            Directory.CreateDirectory(Path.Combine(paths.Mods, n));
            File.WriteAllText(Path.Combine(paths.Mods, n, n + ".package"), "x");
        }
        var a = lib.Scan().First(m => m.Name == "A");
        Assert.ThrowsAny<InvalidOperationException>(() => lib.Rename(a, "B"));
    }

    [Fact]
    public void GuessesAuthor()
    {
        Assert.Equal("Simstrouble", CasPackageEditor.GuessAuthor("[Simstrouble] Wonderland Dress", []));
        Assert.Equal("linavees", CasPackageEditor.GuessAuthor("Training top", ["linavees_yfTop_Black_1"]));
        Assert.Null(CasPackageEditor.GuessAuthor("Top", ["yfTop_Black_1"]));
    }

    [Fact]
    public void ReadsSwatchThumbnailsByInstance()
    {
        using var t = new TestFolder();
        var path = Fake.Package(Path.Combine(t.Downloads, "x.package"),
            new(new ResourceKey(CasPart.ResourceType, 0, 7), Fake.CasPart(CasPart.BodyTop, AgeGender.Female | AgeGender.Adult)),
            new(new ResourceKey(0x3C1AF1F2, 0, 7), Fake.Thumbnail(), Compress: false));

        var thumbs = CasPackageEditor.ReadSwatchThumbnails(path, [7UL, 8UL]);
        Assert.Single(thumbs);
        Assert.True(thumbs.ContainsKey(7));
    }
}
