using SimsModManager.Core;
using SimsModManager.Core.Helper;

namespace SimsModManager.Tests;

/// <summary>Переименование Sims Mod Manager → Ancora: данные и мод-помощник переезжают, ничего не теряется.</summary>
public class RenameTests
{
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void RoamingFolderMovesWhole()
    {
        using var t = new TestFolder();
        var roaming = Path.Combine(t.Root, "Roaming");
        var local = Path.Combine(t.Root, "Local");
        Write(Path.Combine(roaming, "SimsModManager", "settings.json"), "{}");
        Write(Path.Combine(roaming, "SimsModManager", "Checkpoints", "a", "save.zip"), "zip");

        Assert.Empty(AppIdentity.MigrateLegacyFolders(roaming, local));

        Assert.Equal("{}", File.ReadAllText(Path.Combine(roaming, "Ancora", "settings.json")));
        Assert.True(File.Exists(Path.Combine(roaming, "Ancora", "Checkpoints", "a", "save.zip")));
        Assert.False(Directory.Exists(Path.Combine(roaming, "SimsModManager")));
    }

    [Fact]
    public void ExistingNewDataIsNotOverwritten()
    {
        using var t = new TestFolder();
        var roaming = Path.Combine(t.Root, "Roaming");
        Write(Path.Combine(roaming, "SimsModManager", "settings.json"), "old");
        Write(Path.Combine(roaming, "SimsModManager", "Checkpoints", "a", "save.zip"), "a");
        Write(Path.Combine(roaming, "Ancora", "settings.json"), "new");
        Write(Path.Combine(roaming, "Ancora", "Checkpoints", "b", "save.zip"), "b");

        AppIdentity.MigrateLegacyFolders(roaming, Path.Combine(t.Root, "Local"));

        Assert.Equal("new", File.ReadAllText(Path.Combine(roaming, "Ancora", "settings.json")));
        Assert.True(File.Exists(Path.Combine(roaming, "Ancora", "Checkpoints", "a", "save.zip")));
        Assert.True(File.Exists(Path.Combine(roaming, "Ancora", "Checkpoints", "b", "save.zip")));
    }

    [Fact]
    public void LocalFolderKeepsInstallerFiles()
    {
        using var t = new TestFolder();
        var local = Path.Combine(t.Root, "Local");
        var old = Path.Combine(local, "SimsModManager");
        Write(Path.Combine(old, "Update.exe"), "velopack");
        Write(Path.Combine(old, "current", "Ancora.exe"), "app");
        Write(Path.Combine(old, "cache", "content.json"), "{}");
        Write(Path.Combine(old, "originals", "Mod", "a.package"), "orig");

        AppIdentity.MigrateLegacyFolders(Path.Combine(t.Root, "Roaming"), local);

        Assert.True(File.Exists(Path.Combine(old, "Update.exe")));
        Assert.True(File.Exists(Path.Combine(old, "current", "Ancora.exe")));
        Assert.False(Directory.Exists(Path.Combine(old, "cache")));
        Assert.True(File.Exists(Path.Combine(local, "Ancora", "cache", "content.json")));
        Assert.Equal("orig", File.ReadAllText(Path.Combine(local, "Ancora", "originals", "Mod", "a.package")));
    }

    [Fact]
    public void NothingToMigrateIsFine()
    {
        using var t = new TestFolder();
        Assert.Empty(AppIdentity.MigrateLegacyFolders(Path.Combine(t.Root, "R"), Path.Combine(t.Root, "L")));
    }

    [Fact]
    public void HelperInstallRemovesLegacyHelper()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var legacy = Path.Combine(paths.Mods, "SimsModManager");
        Write(Path.Combine(legacy, "SimsModManagerHelper.ts4script"), "old helper");
        Write(Path.Combine(legacy, "bridge", "status.json"), "{}");
        var bundled = Path.Combine(t.Root, HelperMod.FileName);
        File.WriteAllText(bundled, "helper");

        Assert.Empty(new ModLibrary(paths).Scan());   // старая папка помощника тоже не мод
        Assert.True(HelperMod.Install(paths, bundled));

        Assert.False(Directory.Exists(legacy));
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Ancora", "AncoraHelper.ts4script")));
        Assert.Empty(new ModLibrary(paths).Scan());
    }
}
