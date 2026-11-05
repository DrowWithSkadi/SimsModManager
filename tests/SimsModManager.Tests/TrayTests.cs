using SimsModManager.Core;
using SimsModManager.Core.Archives;
using SimsModManager.Core.Content;

namespace SimsModManager.Tests;

public class TrayTests
{
    private const string LotId = "0x00000002!0x0a1b2c3d4e5f6071";

    private static (ModInstaller, ModLibrary, SimsPaths) Create(TestFolder t)
    {
        var paths = new SimsPaths(t.Sims);
        return (new ModInstaller(paths, new SharpCompressExtractor()), new ModLibrary(paths), paths);
    }

    [Fact]
    public void LotGoesToTrayAndItsCcGoesToMods()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        var zip = t.MakeZip("Cozy House.zip",
            ($"Tray/{LotId}.trayitem", "tray"),
            ($"Tray/{LotId}.blueprint", "bp"),
            ($"Tray/{LotId}.bpi", "img"),
            ("Mods/sofa.package", "pkg"));

        var result = installer.InstallOne(zip);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.TrayFileCount);
        Assert.Equal(TrayKind.Lot, result.TrayKind);
        // Файлы участка — прямо в Tray, без подпапок
        Assert.True(File.Exists(Path.Combine(paths.Tray, LotId + ".trayitem")));
        Assert.True(File.Exists(Path.Combine(paths.Tray, LotId + ".blueprint")));
        // Мебель — в Mods
        Assert.True(File.Exists(Path.Combine(paths.Mods, "Cozy House", "sofa.package")));
        Assert.False(File.Exists(Path.Combine(paths.Mods, "Cozy House", LotId + ".trayitem")));

        var items = library.Scan();
        Assert.Equal(2, items.Count);
        var lot = Assert.Single(items, i => i.IsTray);
        Assert.Equal(TrayKind.Lot, lot.TrayKind);
        Assert.Equal(3, lot.Files.Count);
    }

    [Fact]
    public void ArchiveWithOnlyHouseholdWorks()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        var zip = t.MakeZip("Family.zip", ("a.householdbinary", "x"), ("a.trayitem", "x"), ("a.hhi", "x"));

        var result = installer.InstallOne(zip);

        Assert.Equal(InstallStatus.Installed, result.Status);
        Assert.Equal(TrayKind.Household, result.TrayKind);
        Assert.False(Directory.Exists(Path.Combine(paths.Mods, "Family")));
        Assert.Equal(TrayKind.Household, Assert.Single(library.Scan()).TrayKind);
    }

    [Fact]
    public void ReinstallUpdatesTrayEntry()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        installer.InstallOne(t.MakeZip("Room.zip", ("old.room", "1"), ("old.trayitem", "1")));
        File.Delete(Path.Combine(t.Downloads, "Room.zip"));

        var result = installer.InstallOne(t.MakeZip("Room.zip", ("new.room", "2"), ("new.trayitem", "2")));

        Assert.Equal(InstallStatus.Updated, result.Status);
        Assert.False(File.Exists(Path.Combine(paths.Tray, "old.room")));
        Assert.True(File.Exists(Path.Combine(paths.Tray, "new.room")));
        Assert.Equal(2, Assert.Single(library.Scan()).Files.Count);
    }

    [Fact]
    public void DisableMovesTrayFilesOutAndBack()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        installer.InstallOne(t.MakeZip("Lot.zip", ("l.blueprint", "1"), ("l.trayitem", "1")));

        var lot = Assert.Single(library.Scan());
        var off = library.SetEnabled(lot, false);
        Assert.False(File.Exists(Path.Combine(paths.Tray, "l.blueprint")));
        Assert.True(File.Exists(Path.Combine(paths.DisabledTray, "l.blueprint")));
        Assert.False(Assert.Single(library.Scan()).IsEnabled);

        library.SetEnabled(off, true);
        Assert.True(File.Exists(Path.Combine(paths.Tray, "l.blueprint")));
        Assert.True(Assert.Single(library.Scan()).IsEnabled);
    }

    [Fact]
    public void DeleteTrayItemCanBeUndone()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        installer.InstallOne(t.MakeZip("Lot.zip", ("l.blueprint", "1"), ("l.trayitem", "1")));

        var trashed = library.MoveToTrash(Assert.Single(library.Scan()));
        Assert.Empty(library.Scan());
        Assert.False(File.Exists(Path.Combine(paths.Tray, "l.trayitem")));

        library.UndoDelete(trashed);
        Assert.Single(library.Scan());
        Assert.True(File.Exists(Path.Combine(paths.Tray, "l.trayitem")));

        library.FinishDelete(library.MoveToTrash(Assert.Single(library.Scan())));
        Assert.Empty(library.Scan());
        Assert.False(Directory.Exists(Path.Combine(paths.Root, ".modmanager-trash")));
    }

    [Fact]
    public void UserOwnTrayFilesAreNotTouched()
    {
        using var t = new TestFolder();
        var (installer, library, paths) = Create(t);
        Directory.CreateDirectory(paths.Tray);
        File.WriteAllText(Path.Combine(paths.Tray, "my_own_build.trayitem"), "mine");

        installer.InstallOne(t.MakeZip("Lot.zip", ("l.blueprint", "1"), ("l.trayitem", "1")));
        library.FinishDelete(library.MoveToTrash(Assert.Single(library.Scan())));

        Assert.True(File.Exists(Path.Combine(paths.Tray, "my_own_build.trayitem")));
    }

    [Fact]
    public void TrayItemGetsTrayCategoryAndPreview()
    {
        using var t = new TestFolder();
        var bpi = Path.Combine(t.Downloads, "x.bpi");
        var bytes = new byte[40];
        bytes[10] = 0xFF; bytes[11] = 0xD8; bytes[12] = 0xFF; // картинка начинается после заголовка
        File.WriteAllBytes(bpi, bytes);
        var trayitem = Path.Combine(t.Downloads, "x.trayitem");
        File.WriteAllText(trayitem, "x");

        var (content, thumb) = ModAnalyzer.Analyze([bpi, trayitem]);

        Assert.Equal(ModCategory.Tray, content.Category);
        Assert.NotNull(thumb);
        Assert.Equal(30, thumb!.Image.Length);
    }
}
