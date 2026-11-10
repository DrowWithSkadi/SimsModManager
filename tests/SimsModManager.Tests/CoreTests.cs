using System.IO.Compression;
using SimsModManager.Core;
using SimsModManager.Core.Archives;

namespace SimsModManager.Tests;

public class GameLocatorTests
{
    [Fact]
    public void FindsSimsFolderInDocuments()
    {
        using var t = new TestFolder();
        Assert.Equal(Path.GetFullPath(t.Sims), GameLocator.FindSimsFolder([t.Documents]));
    }

    [Fact]
    public void FindsLocalizedFolderName()
    {
        using var t = new TestFolder();
        var german = Path.Combine(t.Documents, "Electronic Arts", "Die Sims 4");
        Directory.Move(t.Sims, german);
        Assert.Equal(Path.GetFullPath(german), GameLocator.FindSimsFolder([t.Documents]));
    }

    [Fact]
    public void ReturnsNullWhenNothingFound()
    {
        using var t = new TestFolder();
        Assert.Null(GameLocator.FindSimsFolder([t.Downloads]));
    }
}

public class InstallerTests
{
    private static (ModInstaller Installer, ModLibrary Library, SimsPaths Paths) Create(TestFolder t)
    {
        var paths = new SimsPaths(t.Sims);
        return (new ModInstaller(paths, new SharpCompressExtractor()), new ModLibrary(paths), paths);
    }

    [Fact]
    public void FlattensNestedFoldersSoScriptsAreOneLevelDeep()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        var zip = t.MakeZip("Cool Mod v2.zip",
            ("CoolMod/Scripts/cool.ts4script", "script"),
            ("CoolMod/Package/cool.package", "pkg"),
            ("CoolMod/readme.txt", "hello"));

        var result = installer.InstallOne(zip);

        Assert.Equal(InstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Cool Mod v2", "cool.ts4script")));
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Cool Mod v2", "cool.package")));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "Cool Mod v2", "readme.txt")));

        var mod = Assert.Single(library.Scan());
        Assert.Equal(1, mod.ScriptCount);
        Assert.Equal(1, mod.PackageCount);
        Assert.Empty(mod.Problems);
    }

    [Fact]
    public void ReinstallReplacesOldVersion()
    {
        using var t = new TestFolder();
        var (installer, _, paths) = Create(t);
        installer.InstallOne(t.MakeZip("Hair.zip", ("old.package", "1")));
        File.Delete(Path.Combine(t.Downloads, "Hair.zip"));

        var result = installer.InstallOne(t.MakeZip("Hair.zip", ("new.package", "2")));

        Assert.Equal(InstallStatus.Updated, result.Status);
        var files = Directory.GetFiles(Path.Combine(paths.Mods, "Hair")).Select(Path.GetFileName);
        Assert.Equal(["new.package"], files);
    }

    [Fact]
    public void ExtractsArchivesInsideArchives()
    {
        using var t = new TestFolder();
        var (installer, _, paths) = Create(t);
        var inner = t.MakeZip("inner.zip", ("dress.package", "x"));
        var outer = Path.Combine(t.Downloads, "Pack.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(inner, "files/inner.zip");

        var result = installer.InstallOne(outer);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Pack", "dress.package")));
    }

    [Fact]
    public void SameFileNameInDifferentFoldersIsKept()
    {
        using var t = new TestFolder();
        var (installer, _, paths) = Create(t);
        installer.InstallOne(t.MakeZip("Set.zip", ("a/item.package", "aaa"), ("b/item.package", "bbbbbb")));

        Assert.True(File.Exists(Path.Combine(paths.Mods, "Set", "item.package")));
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Set", "item (2).package")));
    }

    [Fact]
    public void ArchiveWithoutModsFails()
    {
        using var t = new TestFolder();
        var (installer, _, paths) = Create(t);
        var result = installer.InstallOne(t.MakeZip("Junk.zip", ("readme.txt", "x")));

        Assert.Equal(InstallStatus.Failed, result.Status);
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "Junk")));
    }

    [Fact]
    public void BrokenArchiveFailsGracefully()
    {
        using var t = new TestFolder();
        var (installer, _, _) = Create(t);
        var path = Path.Combine(t.Downloads, "broken.zip");
        File.WriteAllText(path, "это не архив");

        Assert.Equal(InstallStatus.Failed, installer.InstallOne(path).Status);
    }

    [Fact]
    public void InstallsLoosePackageFile()
    {
        using var t = new TestFolder();
        var (installer, _, paths) = Create(t);
        var file = Path.Combine(t.Downloads, "shoes.package");
        File.WriteAllText(file, "x");

        Assert.True(installer.InstallOne(file).Succeeded);
        Assert.True(File.Exists(Path.Combine(paths.Mods, "shoes", "shoes.package")));
        Assert.True(File.Exists(file)); // оригинал не трогаем
    }

    [Fact]
    public void BlocksPathTraversal()
    {
        Assert.Null(SafePath.Resolve("/tmp/a", "../../etc/evil.package"));
        Assert.NotNull(SafePath.Resolve("/tmp/a", "ok/file.package"));
    }
}

public class LibraryTests
{
    [Fact]
    public void DisableAndEnableMovesModOutOfModsFolder()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var library = new ModLibrary(paths);
        Directory.CreateDirectory(Path.Combine(paths.Mods, "Top"));
        File.WriteAllText(Path.Combine(paths.Mods, "Top", "top.package"), "x");

        var mod = Assert.Single(library.Scan());
        var disabled = library.SetEnabled(mod, false);

        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "Top")));
        Assert.True(File.Exists(Path.Combine(paths.DisabledMods, "Top", "top.package")));
        Assert.False(Assert.Single(library.Scan()).IsEnabled);

        library.SetEnabled(disabled, true);
        Assert.True(Assert.Single(library.Scan()).IsEnabled);
    }

    [Fact]
    public void DetectsScriptTooDeep()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var deep = Path.Combine(paths.Mods, "Bad", "Inner");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "x.ts4script"), "x");

        Assert.NotEmpty(Assert.Single(new ModLibrary(paths).Scan()).Problems);
    }

    [Fact]
    public void DeleteRemovesMod()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var library = new ModLibrary(paths);
        File.WriteAllText(Path.Combine(paths.Mods, "loose.package"), "x");

        library.Delete(Assert.Single(library.Scan()));
        Assert.Empty(library.Scan());
    }
}

public class GameOptionsTests
{
    [Fact]
    public void DetectsAndFixesDisabledMods()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        File.WriteAllText(paths.OptionsIni, "[options]\r\nmodsdisabled = 1\r\nscriptmodsenabled = 0\r\nother = 5\r\n");

        Assert.False(GameOptions.ReadModSettings(paths).AllGood);
        GameOptions.EnableMods(paths);

        Assert.True(GameOptions.ReadModSettings(paths).AllGood);
        Assert.Equal("[options]\r\nmodsdisabled = 0\r\nscriptmodsenabled = 1\r\nother = 5\r\n", File.ReadAllText(paths.OptionsIni));
    }
}

public class LaunchAndBackupTests
{
    [Fact]
    public void ParsesSteamLibraries()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"		"C:\\Program Files (x86)\\Steam" }
                "1" { "path"		"D:\\SteamLibrary" }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameLauncher.SteamLibraryFolders(vdf).ToArray());
    }

    [Fact]
    public void DetectsSteamInstall()
    {
        Assert.True(GameLauncher.From(@"D:\SteamLibrary\steamapps\common\The Sims 4\Game\Bin\TS4_x64.exe").IsSteam);
        Assert.False(GameLauncher.From(@"C:\Program Files\EA Games\The Sims 4\Game\Bin\TS4_x64.exe").IsSteam);
    }

    [Fact]
    public void BackupKeepsLatestAndSkipsUnchanged()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var backups = Path.Combine(t.Root, "backups");

        Assert.Null(SaveBackup.Create(paths, backups)); // сохранений нет

        Directory.CreateDirectory(paths.Saves);
        var save = Path.Combine(paths.Saves, "Slot_00000001.save");
        File.WriteAllText(save, "family");

        var first = SaveBackup.Create(paths, backups, keep: 2, now: new DateTime(2026, 1, 1, 10, 0, 0));
        Assert.NotNull(first);
        Assert.Equal(first, SaveBackup.Create(paths, backups, keep: 2, now: new DateTime(2026, 1, 1, 10, 5, 0))); // ничего не менялось

        for (var i = 1; i <= 3; i++)
        {
            File.WriteAllText(save, "family " + i);
            File.SetLastWriteTimeUtc(save, DateTime.UtcNow.AddMinutes(i));
            SaveBackup.Create(paths, backups, keep: 2, now: new DateTime(2026, 1, 1, 11, i, 0));
        }
        var list = SaveBackup.List(backups);
        Assert.Equal(2, list.Count);
        Assert.EndsWith("saves-2026-01-01_11-03-00.zip", list[0]);
        using var zip = System.IO.Compression.ZipFile.OpenRead(list[0]);
        Assert.Contains(zip.Entries, e => e.FullName == "Slot_00000001.save");
    }
}

public class GamePackTests
{
    [Fact]
    public void KnowsSeasons()
    {
        Assert.Equal("EP05", GamePacks.FromId(26)!.Code);
        Assert.Equal("Времена года", GamePacks.NameOf("EP05"));
        Assert.Equal(GamePacks.All.Count, GamePacks.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void ScanDetectsFullAndPartialPacks()
    {
        using var t = new TestFolder();
        var root = Path.Combine(t.Root, "The Sims 4");
        Directory.CreateDirectory(Path.Combine(root, "EP05"));
        File.WriteAllText(Path.Combine(root, "EP05", "ClientFullBuild0.package"), "x");
        Directory.CreateDirectory(Path.Combine(root, "GP01"));
        File.WriteAllText(Path.Combine(root, "GP01", "ClientDeltaBuild0.package"), "x");
        Directory.CreateDirectory(Path.Combine(root, "Data"));

        var packs = GamePacks.Scan(root);
        Assert.Equal(PackState.Installed, packs["EP05"]);
        Assert.Equal(PackState.Partial, packs["GP01"]);
        Assert.False(packs.ContainsKey("DATA"));
    }

    [Fact]
    public void InstallRootFromExe()
    {
        var game = GameLauncher.From(Path.Combine("C:", "Games", "The Sims 4", "Game", "Bin", "TS4_x64.exe"));
        Assert.Equal(Path.Combine("C:", "Games", "The Sims 4"), GameLauncher.InstallRoot(game));
    }
}
