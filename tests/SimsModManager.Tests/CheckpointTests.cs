using System.Text.Json;
using SimsModManager.Core;
using SimsModManager.Core.Helper;
using SimsModManager.Core.Saves;

namespace SimsModManager.Tests;

public class CheckpointTests
{
    private static string FakeSave(string folder, string name, byte fill)
    {
        Directory.CreateDirectory(folder);
        var bytes = new byte[2048];
        Array.Fill(bytes, fill);
        "DBPF"u8.CopyTo(bytes);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void CreatesAndRestoresCheckpoint()
    {
        using var t = new TestFolder();
        var saves = Path.Combine(t.Sims, "saves");
        var slot = FakeSave(saves, "Slot_00000002.save", 1);
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));

        var cp = store.Create(slot, "Перед оборотнями", CheckpointKind.Manual, "Семья Гот");
        FakeSave(saves, "Slot_00000002.save", 2); // игрок играл дальше

        store.Restore(cp, saves);
        Assert.Equal(1, File.ReadAllBytes(slot)[100]);
        var listed = Assert.Single(store.List());
        Assert.Equal("Перед оборотнями", listed.Name);
        Assert.Equal("Slot_00000002.save", listed.SlotFile);
        Assert.Equal("Семья Гот", listed.GameSaveName);
    }

    [Fact]
    public void RejectsBrokenSave()
    {
        using var t = new TestFolder();
        var path = Path.Combine(t.Root, "Slot_00000003.save");
        File.WriteAllText(path, "");
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));
        Assert.Throws<InvalidSaveException>(() => store.Create(path, "x", CheckpointKind.Manual));
        Assert.Empty(store.List());
    }

    [Fact]
    public void SkipsDuplicateWhenAsked()
    {
        using var t = new TestFolder();
        var slot = FakeSave(Path.Combine(t.Sims, "saves"), "Slot_00000002.save", 1);
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));
        var a = store.Create(slot, "", CheckpointKind.Auto, now: new DateTime(2026, 1, 1, 10, 0, 0));
        var b = store.Create(slot, "", CheckpointKind.Auto, now: new DateTime(2026, 1, 1, 10, 5, 0), skipIfUnchanged: true);
        Assert.Equal(a.Id, b.Id);
        Assert.Single(store.List());
        Assert.StartsWith("Автосохранение", a.Name);
    }

    [Fact]
    public void RetentionKeepsPinned()
    {
        using var t = new TestFolder();
        var saves = Path.Combine(t.Sims, "saves");
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));
        var start = new DateTime(2026, 1, 1);
        for (var i = 0; i < 5; i++)
        {
            var slot = FakeSave(saves, "Slot_00000002.save", (byte)(i + 1));
            var cp = store.Create(slot, $"#{i}", CheckpointKind.Manual, now: start.AddDays(i * 10));
            if (i == 0) { cp.Pinned = true; store.Update(cp); }
        }

        // Старше 15 дней на 1 февраля: #0 (закреплён) и #1 — удаляется только #1
        var removed = store.ApplyRetention(new RetentionRules(MaxAgeDays: 15), now: new DateTime(2026, 2, 1));
        Assert.Equal(["#1"], removed.Select(c => c.Name));

        // Не больше двух незакреплённых: остаются #4, #3 и закреплённый #0
        store.ApplyRetention(new RetentionRules(MaxCount: 2), now: new DateTime(2026, 2, 1));
        Assert.Equal(["#4", "#3", "#0"], store.List().Select(c => c.Name));
    }

    [Fact]
    public void NoRulesDeleteNothing()
    {
        using var t = new TestFolder();
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));
        var slot = FakeSave(Path.Combine(t.Sims, "saves"), "Slot_00000002.save", 1);
        store.Create(slot, "a", CheckpointKind.Manual, now: new DateTime(2000, 1, 1));
        Assert.Empty(store.ApplyRetention(new RetentionRules()));
        Assert.Single(store.List());
    }

    [Fact]
    public async Task RollbackWithoutGameKeepsCurrentState()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var store = new CheckpointStore(Path.Combine(t.Root, "cp"));
        var manager = new CheckpointManager(paths, store, new HelperBridge(Path.Combine(t.Root, "bridge")), () => false);

        FakeSave(paths.Saves, "Slot_00000002.save", 1);
        var old = await manager.MakeAsync("Старая");
        FakeSave(paths.Saves, "Slot_00000002.save", 2);

        var steps = new List<RollbackStep>();
        await manager.RollbackAsync(old, new SyncProgress<RollbackStep>(steps.Add));

        Assert.Equal(1, File.ReadAllBytes(Path.Combine(paths.Saves, "Slot_00000002.save"))[100]);
        Assert.Contains(store.List(), c => c.Kind == CheckpointKind.BeforeRollback);
        Assert.Equal(RollbackStep.Done, steps[^1]);
    }

    [Fact]
    public async Task RefusesWhenGameRunsWithoutHelper()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        FakeSave(paths.Saves, "Slot_00000002.save", 1);
        var manager = new CheckpointManager(paths, new CheckpointStore(Path.Combine(t.Root, "cp")),
            new HelperBridge(Path.Combine(t.Root, "bridge")), () => true);
        await Assert.ThrowsAsync<CheckpointException>(() => manager.MakeAsync("x"));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public void SlotFileNameIsHex()
    {
        Assert.Equal("Slot_0000001a.save", SaveSlots.FileName(26));
    }
}

public class HelperTests
{
    [Fact]
    public void HelperFolderHiddenFromModList()
    {
        using var t = new TestFolder();
        var paths = new SimsPaths(t.Sims);
        var bundled = Path.Combine(t.Root, HelperMod.FileName);
        File.WriteAllText(bundled, "helper");
        Assert.True(HelperMod.Install(paths, bundled));
        Assert.False(HelperMod.Install(paths, bundled)); // тот же файл — ничего не меняется
        Assert.True(HelperMod.IsInstalled(paths));
        Assert.Empty(new ModLibrary(paths).Scan());
    }

    [Fact]
    public void StatusIsOfflineWhenStale()
    {
        using var t = new TestFolder();
        var bridge = new HelperBridge(Path.Combine(t.Root, "bridge"));
        Assert.Equal(HelperState.Offline, bridge.ReadStatus().State);

        Directory.CreateDirectory(bridge.Folder);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        File.WriteAllText(Path.Combine(bridge.Folder, "status.json"),
            JsonSerializer.Serialize(new { state = "live", slot_id = 2, slot_name = "Гот", version = 1, time = now }));
        var status = bridge.ReadStatus();
        Assert.Equal(HelperState.Live, status.State);
        Assert.Equal(2, status.SlotId);
        Assert.Equal("Гот", status.SlotName);

        Assert.Equal(HelperState.Offline, bridge.ReadStatus(DateTime.UtcNow.AddSeconds(30)).State);
    }

    [Fact]
    public async Task CommandTimesOutWithoutGame()
    {
        using var t = new TestFolder();
        var bridge = new HelperBridge(Path.Combine(t.Root, "bridge"));
        var response = await bridge.SendAsync("ping", TimeSpan.FromMilliseconds(200));
        Assert.False(response.Ok);
        Assert.Empty(Directory.GetFiles(bridge.Folder)); // команда не осталась висеть
    }

    [Fact]
    public async Task ReadsResponse()
    {
        using var t = new TestFolder();
        var bridge = new HelperBridge(Path.Combine(t.Root, "bridge"));
        var fakeGame = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                var cmd = Directory.Exists(bridge.Folder) ? Directory.GetFiles(bridge.Folder, "cmd-*.json").FirstOrDefault() : null;
                if (cmd is not null)
                {
                    var id = JsonDocument.Parse(File.ReadAllText(cmd)).RootElement.GetProperty("id").GetString();
                    File.Delete(cmd);
                    File.WriteAllText(Path.Combine(bridge.Folder, $"res-{id}.json"),
                        JsonSerializer.Serialize(new { id, ok = true, result = new { paused = true } }));
                    return;
                }
                await Task.Delay(20);
            }
        });
        var response = await bridge.SendAsync("ping", TimeSpan.FromSeconds(3));
        await fakeGame;
        Assert.True(response.Ok);
        Assert.True(response.Result!["paused"]!.GetValue<bool>());
    }
}
