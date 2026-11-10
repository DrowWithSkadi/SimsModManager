using System.IO.Compression;
using System.Text;
using SimsModManager.Core;
using SimsModManager.Core.Dbpf;
using SimsModManager.Core.Health;

namespace SimsModManager.Tests;

public class HealthTests
{
    private static readonly ResourceKey Tuning = new(0x03B33DDF, 0, 0x1111);
    private static readonly ResourceKey Texture = new(0x00B2D882, 0, 0x2222);
    private static readonly ResourceKey Thumb = new(0x3C1AF1F2, 0, 0x3333);

    private static DbpfWriter.Resource Res(ResourceKey key, string data) => new(key, Encoding.UTF8.GetBytes(data));

    private static string Script(string path, params string[] modules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var m in modules)
        {
            using var w = new StreamWriter(zip.CreateEntry(m).Open());
            w.Write("code " + m + path);
        }
        return path;
    }

    private static (ModLibrary, SimsPaths, ModHealthChecker) Setup(TestFolder t)
    {
        var paths = new SimsPaths(t.Sims);
        return (new ModLibrary(paths), paths, new ModHealthChecker(paths));
    }

    [Fact]
    public void FindsGameplayConflictBetweenTwoMods()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Fake.Package(Path.Combine(paths.Mods, "Better Cooking", "a.package"), Res(Tuning, "<I v=1/>"), Res(Thumb, "x"));
        Fake.Package(Path.Combine(paths.Mods, "Chef Mod", "b.package"), Res(Tuning, "<I v=2/>"), Res(Thumb, "y"));

        var report = checker.Check(lib.Scan());

        var issue = Assert.Single(report.Issues);
        Assert.Equal(HealthIssueKind.Conflict, issue.Kind);
        Assert.Equal(HealthSeverity.Problem, issue.Severity);
        Assert.Equal(2, issue.Mods.Count);
        Assert.Equal(1, issue.Count); // миниатюры не считаются
    }

    [Fact]
    public void IdenticalSharedResourcesAreNotAConflict()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Fake.Package(Path.Combine(paths.Mods, "Dress A", "a.package"), Res(Texture, "same"), Res(new ResourceKey(1, 0, 1), "a"));
        Fake.Package(Path.Combine(paths.Mods, "Dress B", "b.package"), Res(Texture, "same"), Res(new ResourceKey(1, 0, 2), "b"));

        Assert.Empty(checker.Check(lib.Scan()).Issues);
    }

    [Fact]
    public void DisabledModsAreNotChecked()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Fake.Package(Path.Combine(paths.Mods, "One", "a.package"), Res(Tuning, "1"));
        Fake.Package(Path.Combine(paths.DisabledMods, "Two", "b.package"), Res(Tuning, "2"));

        Assert.Empty(checker.Check(lib.Scan()).Issues);
    }

    [Fact]
    public void TwoVersionsOfSameModSuggestRemovingOlder()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        var oldFile = Fake.Package(Path.Combine(paths.Mods, "CoolMod v1", "cool.package"), Res(Tuning, "old"));
        Fake.Package(Path.Combine(paths.Mods, "CoolMod v2", "cool2.package"), Res(Tuning, "new version"));
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

        var issue = Assert.Single(checker.Check(lib.Scan()).Issues);
        Assert.Equal("CoolMod v1", issue.Suggested?.Name);
        Assert.Contains("более новую", issue.Advice);
    }

    [Fact]
    public void DuplicateInstallIsReportedOnceNotAsConflict()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Fake.Package(Path.Combine(paths.Mods, "Hair", "hair.package"), Res(Tuning, "x"));
        Fake.Package(Path.Combine(paths.Mods, "Hair (2)", "hair.package"), Res(Tuning, "x"));

        var issue = Assert.Single(checker.Check(lib.Scan()).Issues);
        Assert.Equal(HealthIssueKind.Duplicate, issue.Kind);
        Assert.NotNull(issue.Suggested);
    }

    [Fact]
    public void FindsScriptConflict()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Script(Path.Combine(paths.Mods, "UI Cheats", "ui_cheats.ts4script"), "uicheats/main.pyc", "uicheats/util.pyc");
        Script(Path.Combine(paths.Mods, "UI Cheats NEW", "ui_cheats_new.ts4script"), "uicheats/main.pyc", "uicheats/extra.pyc");

        var issue = Assert.Single(checker.Check(lib.Scan()).Issues);
        Assert.Equal(HealthIssueKind.ScriptConflict, issue.Kind);
        Assert.Equal(1, issue.Count);
    }

    [Fact]
    public void FindsAndFixesTooDeepScript()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Script(Path.Combine(paths.Mods, "Deep", "inner", "folder", "deep.ts4script"), "deep/a.pyc");
        Fake.Package(Path.Combine(paths.Mods, "Deep", "inner", "ok.package"), Res(Tuning, "x"));

        var mod = Assert.Single(lib.Scan());
        var issue = Assert.Single(checker.Check([mod]).Issues);
        Assert.Equal(HealthIssueKind.TooDeep, issue.Kind);

        var fixedMod = lib.FixTooDeep(mod);
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Deep", "deep.ts4script")));
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Deep", "inner", "ok.package"))); // .package глубина 2 — можно
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "Deep", "inner", "folder")));
        Assert.Empty(checker.Check(lib.Scan()).Issues);
        Assert.Equal(2, fixedMod.Files.Count);
    }

    [Fact]
    public void FindsBrokenPackageAndArchive()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Directory.CreateDirectory(Path.Combine(paths.Mods, "Broken"));
        File.WriteAllText(Path.Combine(paths.Mods, "Broken", "half.package"), "DBPF but cut");
        File.Copy(t.MakeZip("pack.zip", ("x.package", "x")), Path.Combine(paths.Mods, "pack.zip"));

        var kinds = checker.Check(lib.Scan()).Issues.Select(i => i.Kind).Order().ToList();
        Assert.Equal([HealthIssueKind.Broken, HealthIssueKind.ArchiveInMods], kinds);
    }

    [Fact]
    public void ErrorReportPointsToMod()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        var script = Script(Path.Combine(paths.Mods, "Broken Mod", "broken.ts4script"), "brokenmod/core/main.pyc");
        Script(Path.Combine(paths.Mods, "Fine Mod", "fine.ts4script"), "finemod/main.pyc");
        File.SetLastWriteTime(script, DateTime.Now.AddDays(-2));
        File.WriteAllText(Path.Combine(paths.Root, "lastException_1.txt"), """
            <root><report><desyncdata>Traceback (most recent call last):
              File "T:\InGame\Gameplay\Scripts\Server\interactions\base\interaction.py", line 10, in run
              File "D:\Dev\Mods\brokenmod\core\main.py", line 42, in on_start
            AttributeError: 'NoneType' object has no attribute 'sim_info'
            </desyncdata></report></root>
            """);
        File.WriteAllText(Path.Combine(paths.Root, "lastUIException.txt"), """
            File "T:\InGame\Gameplay\Scripts\Server\ui\ui_dialog.py", line 5
            KeyError: 12
            """);

        var issues = checker.Check(lib.Scan()).Issues;
        var culprit = Assert.Single(issues, i => i.Kind == HealthIssueKind.CausesErrors);
        Assert.Equal("Broken Mod", Assert.Single(culprit.Mods).Name);
        Assert.Contains("AttributeError", culprit.Details);
        Assert.Single(issues, i => i.Kind == HealthIssueKind.UnknownErrors);
    }

    [Fact]
    public void ErrorReportOlderThanModUpdateIsIgnored()
    {
        using var t = new TestFolder();
        var (lib, paths, checker) = Setup(t);
        Script(Path.Combine(paths.Mods, "Updated", "u.ts4script"), "updmod/a.pyc");
        var report = Path.Combine(paths.Root, "lastException.txt");
        File.WriteAllText(report, @"File ""C:\x\updmod\a.py"", line 1");
        File.SetLastWriteTime(report, DateTime.Now.AddDays(-5));

        Assert.DoesNotContain(checker.Check(lib.Scan()).Issues, i => i.Kind == HealthIssueKind.CausesErrors);
    }

    [Fact]
    public void ReadsGameVersion()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        Assert.Null(GameVersion.Read(paths));
        File.WriteAllBytes(GameVersion.FilePath(paths), [0x0F, 0, 0, 0, .. Encoding.ASCII.GetBytes("1.109.185.1030")]);
        Assert.Equal("1.109.185.1030", GameVersion.Read(paths));
    }

    [Fact]
    public void SameModNameHeuristic()
    {
        Assert.True(ModHealthChecker.LooksLikeSameMod("WickedWhims v170", "WickedWhims_v172b"));
        Assert.False(ModHealthChecker.LooksLikeSameMod("Better Cooking", "Chef Mod"));
    }
}

public class BisectTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(100)]
    public void FindsCulpritInLogSteps(int count)
    {
        var mods = Enumerable.Range(0, count).Select(i => $"mod{i:000}").ToList();
        for (var culpritIndex = 0; culpritIndex < count; culpritIndex += Math.Max(1, count / 7))
        {
            var culprit = mods[culpritIndex];
            var b = ModBisect.Start(mods);
            var steps = 0;
            while (!b.IsFinished)
            {
                var enabled = b.ShouldBeEnabled();
                b.Answer(enabled.Contains(culprit));
                steps++;
            }
            Assert.Equal(culprit, b.Culprit);
            Assert.True(steps <= Math.Ceiling(Math.Log2(Math.Max(2, count))));
        }
    }

    [Fact]
    public void KeepsInnocentModsEnabledAndSaves()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var b = ModBisect.Start(["a", "b", "c", "d"], suspects: ["c", "d"]);
        var enabled = b.ShouldBeEnabled();
        Assert.Contains("a", enabled);
        Assert.Contains("b", enabled);
        Assert.Equal(3, enabled.Count);

        b.Save(paths);
        var loaded = ModBisect.Load(paths)!;
        Assert.Equal(b.TestingNow, loaded.TestingNow);
        ModBisect.Clear(paths);
        Assert.Null(ModBisect.Load(paths));
    }
}

public class HealthKeyTests
{
    [Fact]
    public void FingerprintMakesNewErrorsShowAgain()
    {
        HealthIssue Unknown(string fp) => new(HealthIssueKind.UnknownErrors, HealthSeverity.Warning, "t", "e", "a", []) { Fingerprint = fp };

        Assert.Equal(Unknown("last-20261108123600").Key, Unknown("last-20261108123600").Key);
        Assert.NotEqual(Unknown("last-20261108123600").Key, Unknown("last-20261110090000").Key);
    }
}

public class ErrorMentionTests
{
    private static ModItem Mod(string name, params string[] files) =>
        new(name, @"C:\Mods\" + name, true, true, files.Select(f => @"C:\Mods\" + name + @"\" + f).ToList(), 1, DateTime.MinValue, []);

    [Fact]
    public void FindsModByFileNameInReport()
    {
        var mods = new[] { Mod("Hair Pack", "simple_hair.package"), Mod("Better Build", "betterbuild_core.ts4script") };
        var found = ModHealthChecker.FindByMention("Exception in betterbuild_core.ts4script while loading tuning", mods);
        Assert.Equal("Better Build", Assert.Single(found).Name);
    }

    [Fact]
    public void IgnoresShortOrCommonNames()
    {
        var mods = new[] { Mod("hair", "a.package"), Mod("Clothes", "b.package") };
        Assert.Empty(ModHealthChecker.FindByMention("error while drawing hair and clothes", mods));
    }
}
