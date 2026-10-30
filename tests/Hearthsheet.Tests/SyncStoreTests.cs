using Hearthsheet.Core.Application;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Storage;
using Hearthsheet.Core.Sync;
using Hearthsheet.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

/// <summary>A browser-storage stand-in: keeps the records in a dictionary (optionally under a key prefix) and every batch.</summary>
internal sealed class DictionaryJournal(Dictionary<string, string>? data = null, string prefix = "") : IStoreJournal
{
    public Dictionary<string, string> Data { get; } = data ?? [];
    public List<IReadOnlyList<StoreRecord>> Batches { get; } = [];

    public void Write(IReadOnlyList<StoreRecord> batch)
    {
        Batches.Add(batch);
        foreach (var r in batch)
            if (r.Json is null) Data.Remove(prefix + r.Key);
            else Data[prefix + r.Key] = r.Json;
    }

    public IEnumerable<StoreRecord> Saved() => Data
        .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
        .Select(kv => new StoreRecord(kv.Key[prefix.Length..], kv.Value)).ToList();

    public MemoryCharacterStore Open(TimeProvider? time = null) =>
        new(Saved(), this, NullLogger<MemoryCharacterStore>.Instance, time);
}

/// <summary>Behaviour every local character store must share, desktop (SQLite) or browser (memory).</summary>
public abstract class SyncStoreContract<TStore> : IDisposable where TStore : ICharacterRepository, ISyncLocalStore
{
    protected readonly TempDir Dir = new();

    /// <summary>Opens a store; each call reopens the same storage.</summary>
    protected abstract TStore Open();

    public void Dispose() => Dir.Dispose();

    [Fact]
    public void SaveLoadUpdateDelete()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);

        var loaded = repo.Load(c.Id)!;
        Assert.Equal(CharacterJson.Serialize(c), CharacterJson.Serialize(loaded));

        loaded.Identity.Name = "Renamed";
        loaded.TakeDamage(5);
        repo.Save(loaded);
        var summaries = repo.List();
        Assert.Single(summaries);
        Assert.Equal("Renamed", summaries[0].Name);
        Assert.Contains("Wizard 5", summaries[0].Description);
        Assert.Equal(loaded.Combat.CurrentHp, repo.Load(c.Id)!.Combat.CurrentHp);

        Assert.True(repo.Delete(c.Id));
        Assert.Null(repo.Load(c.Id));
        Assert.False(repo.Delete(c.Id));
    }

    [Fact]
    public void Data_SurvivesReopening()
    {
        var c = Samples.Rich();
        Open().Save(c);
        Assert.Equal("Arannis", Open().Load(c.Id)!.Identity.Name);
    }

    [Fact]
    public void MultipleCharacters_AreListedByName()
    {
        var repo = Open();
        foreach (var name in new[] { "zed", "Alice", "bob", "" })
        {
            var c = new Character();
            c.Identity.Name = name;
            repo.Save(c);
        }
        Assert.Equal(["Alice", "bob", "Unnamed character", "zed"], repo.List().Select(s => s.Name));
    }

    [Fact]
    public void Library_DuplicateCreatesIndependentCopy()
    {
        var repo = Open();
        var library = new CharacterLibrary(repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);
        var original = library.Create(new NewCharacterOptions("Hero", "Fighter", 3, "Human", "Soldier"));
        var copy = library.Duplicate(original);

        copy.Abilities.Strength = 20;
        repo.Save(copy);
        Assert.Equal(2, repo.List().Count);
        Assert.Equal(10, repo.Load(original.Id)!.Abilities.Strength);
        Assert.Equal("Hero (copy)", repo.Load(copy.Id)!.Identity.Name);

        var exportPath = Dir.File("hero.dndchar");
        library.Export(original, exportPath);
        var imported = library.Import(exportPath);
        Assert.Equal(3, repo.List().Count);
        Assert.Equal("Hero", imported.Identity.Name);
    }

    [Fact]
    public void ExportAll_WritesOneEntryPerCharacter_AndRoundTrips()
    {
        var repo = Open();
        var library = new CharacterLibrary(repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);
        foreach (var name in new[] { "Arannis", "Arannis", "Bo/rin" })
        {
            var c = new Character();
            c.Identity.Name = name;
            repo.Save(c);
        }

        using var ms = new MemoryStream();
        library.ExportAll(ms);
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);

        var names = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(["Arannis (2).dndchar", "Arannis.dndchar", "Bo_rin.dndchar"], names);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var imported = library.Import(reader.ReadToEnd(), entry.FullName);
            Assert.Equal(entry.FullName.StartsWith("Arannis") ? "Arannis" : "Bo/rin", imported.Identity.Name);
        }
    }

    [Fact]
    public void Save_MarksDirtyAndCountsLocalRevisions()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);
        repo.Save(c);
        Assert.Equal(2, Assert.Single(repo.GetDirty()).LocalRevision);
        Assert.Equal(new LocalState(true, 2), repo.GetState(c.Id));
        Assert.Null(repo.GetState(Guid.NewGuid()));
    }

    [Fact]
    public void Save_OfAPulledRow_KeepsItsCloudRevision()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.ApplyRemote(c, 4);
        repo.Save(c);
        var change = Assert.Single(repo.GetDirty());
        Assert.Equal((1L, (long?)4), (change.LocalRevision, change.CloudRevision));
    }

    [Fact]
    public void MarkPushed_ClearsDirtyOnlyIfNothingWasSavedMeanwhile()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);
        var pushed = Assert.Single(repo.GetDirty());
        repo.Save(c); // saved while the push was in flight

        repo.MarkPushed(c.Id, pushed.LocalRevision, cloudRevision: 1);
        var still = Assert.Single(repo.GetDirty());
        Assert.Equal(1, still.CloudRevision);

        repo.MarkPushed(c.Id, still.LocalRevision, cloudRevision: 2);
        Assert.Empty(Open().GetDirty());
    }

    [Fact]
    public void ApplyRemote_RespectsDirtyRows_UnlessGuardedOrForced()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);
        var remote = CharacterJson.Clone(c);
        remote.Identity.Name = "Remote";

        Assert.False(repo.ApplyRemote(remote, 5));
        Assert.False(repo.ApplyRemote(remote, 5, ifLocalRevision: 99));
        Assert.True(repo.ApplyRemote(remote, 5, ifLocalRevision: 1));
        Assert.Equal("Remote", repo.Load(c.Id)!.Identity.Name);
        Assert.Empty(repo.GetDirty());

        repo.Save(c);
        Assert.True(repo.ApplyRemote(remote, 6, force: true));
        Assert.Equal(6, Open().GetCloudRevisions()[c.Id]);
        Assert.Equal(new LocalState(false, 2), repo.GetState(c.Id)); // the local counter is kept
    }

    [Fact]
    public void ApplyRemote_InsertsNewRows_ButNotOnesDeletedLocally()
    {
        var repo = Open();
        var c = Samples.Rich();
        Assert.True(repo.ApplyRemote(c, 1));
        Assert.Empty(repo.GetDirty());

        Assert.True(repo.Delete(c.Id));
        Assert.Equal(new PendingDelete(c.Id, 1), Assert.Single(Open().GetPendingDeletes()));
        Assert.False(repo.ApplyRemote(c, 2));
        Assert.False(repo.ApplyRemote(c, 2, force: true));
        Assert.Null(repo.Load(c.Id));

        repo.ClearPendingDelete(c.Id);
        Assert.Empty(Open().GetPendingDeletes());
    }

    [Fact]
    public void Delete_OfNeverSyncedCharacter_LeavesNothingPending()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);
        Assert.True(repo.Delete(c.Id));
        Assert.False(repo.Delete(c.Id));
        Assert.Empty(repo.GetPendingDeletes());
    }

    [Fact]
    public void ApplyRemoteDelete_SkipsDirtyRowsUnlessForced()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.Save(c);
        Assert.False(repo.ApplyRemoteDelete(c.Id));
        Assert.True(repo.ApplyRemoteDelete(c.Id, force: true));
        Assert.Null(Open().Load(c.Id));
        Assert.Empty(repo.GetPendingDeletes());
        Assert.False(repo.ApplyRemoteDelete(c.Id, force: true));
    }

    [Fact]
    public void Rebase_SetsCloudRevisionAndMarksDirty()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.ApplyRemote(c, 3);
        repo.Rebase(c.Id, null);
        Assert.Null(Assert.Single(Open().GetDirty()).CloudRevision);
    }

    [Fact]
    public void ReassignAllIds_ThenResetSyncState_StartsOverUnderANewAccount()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.ApplyRemote(c, 3);
        repo.SetSyncAccount("user-1");
        repo.SetLastPull(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        repo.ReassignAllIds();
        repo.ResetSyncState(markAllDirty: true);

        repo = Open();
        var moved = Assert.Single(repo.List());
        Assert.NotEqual(c.Id, moved.Id);
        Assert.Equal(c.Identity.Name, repo.Load(moved.Id)!.Identity.Name);
        Assert.Equal(moved.Id, repo.Load(moved.Id)!.Id);
        Assert.Null(Assert.Single(repo.GetDirty()).CloudRevision);
        Assert.Equal(new SyncMeta("user-1", null), repo.GetSyncMeta());

        repo.ResetSyncState(markAllDirty: false);
        Assert.Empty(Open().GetDirty());
    }

    [Fact]
    public void ResetSyncState_ClearsPendingDeletes()
    {
        var repo = Open();
        var c = Samples.Rich();
        repo.ApplyRemote(c, 3);
        repo.Delete(c.Id);
        repo.ResetSyncState(markAllDirty: true);
        Assert.Empty(Open().GetPendingDeletes());
    }

    [Fact]
    public void SyncMeta_RoundTrips()
    {
        var repo = Open();
        Assert.Equal(new SyncMeta(null, null), repo.GetSyncMeta());
        var at = new DateTimeOffset(2026, 9, 29, 10, 0, 0, 123, TimeSpan.Zero);
        repo.SetSyncAccount("user-1");
        repo.SetLastPull(at);
        Assert.Equal(new SyncMeta("user-1", at), Open().GetSyncMeta());
        repo.SetLastPull(null);
        Assert.Equal(new SyncMeta("user-1", null), Open().GetSyncMeta());
    }
}

public sealed class SqliteSyncStoreTests : SyncStoreContract<SqliteCharacterRepository>
{
    protected override SqliteCharacterRepository Open() =>
        new(Dir.File("characters.db"), NullLogger<SqliteCharacterRepository>.Instance);

    [Fact]
    public void UpgradeFromVersion1_KeepsRowsAndMarksThemUnsynced()
    {
        var c = Samples.Rich();
        UpgradeBackupTests.CreateVersion1Database(Dir.File("characters.db"), c);

        var repo = Open();
        Assert.Equal("Arannis", repo.Load(c.Id)!.Identity.Name);
        Assert.Null(Assert.Single(repo.GetDirty()).CloudRevision);
        Assert.Single(Open().GetDirty()); // reopening at version 2 changes nothing
    }
}

public sealed class MemorySyncStoreTests : SyncStoreContract<MemoryCharacterStore>
{
    private readonly DictionaryJournal _journal = new();

    protected override MemoryCharacterStore Open() => _journal.Open();

    [Fact]
    public void Memory_WritesOneBatchPerChange()
    {
        var store = Open();
        var c = Samples.Rich();
        store.Save(c);
        Assert.Equal([$"c:{c.Id}"], Assert.Single(_journal.Batches).Select(r => r.Key));

        store.MarkPushed(c.Id, 1, 7);
        Assert.Equal(0, store.UnsyncedCount);
        _journal.Batches.Clear();
        Assert.True(store.Delete(c.Id));
        var batch = Assert.Single(_journal.Batches);
        Assert.Equal([($"c:{c.Id}", true), ($"d:{c.Id}", false)], batch.Select(r => (r.Key, r.Json is null)).Order());
        Assert.Equal(1, store.UnsyncedCount);

        _journal.Batches.Clear();
        Assert.False(store.Delete(c.Id));
        Assert.Empty(_journal.Batches);
    }

    [Fact]
    public void Memory_ResetAndReassign_AreOneBatchEach()
    {
        var store = Open();
        store.Save(Samples.Rich());
        store.Save(Samples.Rich());
        store.SetLastPull(DateTimeOffset.UnixEpoch);
        _journal.Batches.Clear();

        store.ReassignAllIds();
        store.ResetSyncState(markAllDirty: false);
        Assert.Equal(2, _journal.Batches.Count);
        Assert.Equal(4, _journal.Batches[0].Count); // two removed, two added
        Assert.Contains(_journal.Batches[1], r => r.Key == "m:sync_last_pull" && r.Json is null);
        Assert.Equal(0, store.UnsyncedCount);
    }

    [Fact]
    public void Memory_SkipsAnUnreadableRecord_WithoutRemovingIt()
    {
        var good = Samples.Rich();
        Open().Save(good);
        var badKey = $"c:{Guid.NewGuid()}";
        _journal.Data[badKey] = "{broken";
        _journal.Data[$"c:{Guid.NewGuid()}"] = """{"Id":"not-a-guid"}""";
        _journal.Data[$"d:{Guid.NewGuid()}"] = "\"x\"";

        var store = Open();
        Assert.Equal(good.Id, Assert.Single(store.List()).Id);
        Assert.Empty(store.GetPendingDeletes());
        store.Save(good);
        Assert.Equal("{broken", _journal.Data[badKey]);
        Assert.Equal(4, _journal.Data.Count); // the good row and all three unreadable ones
    }
}
