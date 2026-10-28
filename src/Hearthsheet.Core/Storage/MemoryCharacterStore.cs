using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Core.Storage;

/// <summary>One persisted record: Json null removes the key.</summary>
public sealed record StoreRecord(string Key, string? Json);

/// <summary>
/// Receives each store change as one batch, in order. Must not block and must not throw: an implementation reports
/// a failed write out of band (for example a <c>Failed</c> flag the UI shows), never by throwing into the store.
/// </summary>
public interface IStoreJournal { void Write(IReadOnlyList<StoreRecord> batch); }

/// <summary>A stored character row, the same columns as the desktop's <c>characters</c> table.</summary>
internal sealed record CharacterRow(
    Guid Id, string Name, string Description, int SchemaVersion, string Data,
    DateTime CreatedUtc, DateTime UpdatedUtc, long? CloudRevision, bool Dirty, long LocalRevision);

[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(CharacterRow))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;

/// <summary>
/// Keeps every character in memory and hands each change to an <see cref="IStoreJournal"/> (IndexedDB in the browser).
/// Each method mirrors one statement or transaction of the desktop's SqliteCharacterRepository and never awaits, so it
/// is atomic. Keys: <c>c:{id}</c> rows, <c>d:{id}</c> pending deletes (their cloud revision), <c>m:{name}</c> sync meta.
/// </summary>
public sealed class MemoryCharacterStore : ICharacterRepository, ISyncLocalStore
{
    private const string AccountKey = "sync_account";
    private const string LastPullKey = "sync_last_pull";

    private readonly Dictionary<Guid, CharacterRow> _rows = [];
    private readonly Dictionary<Guid, long> _pendingDeletes = [];
    private readonly Dictionary<string, string> _meta = [];
    private readonly IStoreJournal _journal;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    public MemoryCharacterStore(IEnumerable<StoreRecord> saved, IStoreJournal journal, ILogger<MemoryCharacterStore> logger, TimeProvider? time = null)
    {
        _journal = journal;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        foreach (var record in saved)
        {
            if (record.Json is null) continue;
            try
            {
                Read(record);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
            {
                // Left in storage: a later version (or a person) may still recover it.
                _logger.LogWarning(ex, "Skipped an unreadable stored record {Key}", record.Key);
            }
        }
    }

    private void Read(StoreRecord record)
    {
        var (kind, name) = (record.Key[..Math.Min(2, record.Key.Length)], record.Key.Length > 2 ? record.Key[2..] : "");
        switch (kind)
        {
            case "c:":
                var row = JsonSerializer.Deserialize(record.Json!, StoreJsonContext.Default.CharacterRow)
                    ?? throw new FormatException("Empty row.");
                if (row.Id != Guid.Parse(name)) throw new FormatException("Row id does not match its key.");
                _rows[row.Id] = row;
                break;
            case "d:":
                _pendingDeletes[Guid.Parse(name)] = JsonSerializer.Deserialize(record.Json!, StoreJsonContext.Default.Int64);
                break;
            case "m:":
                _meta[name] = JsonSerializer.Deserialize(record.Json!, StoreJsonContext.Default.String)
                    ?? throw new FormatException("Empty meta value.");
                break;
            default:
                throw new FormatException("Unknown record kind.");
        }
    }

    /// <summary>Characters waiting to be pushed: dirty rows plus pending deletes.</summary>
    public int UnsyncedCount
    {
        get { lock (_gate) return _rows.Values.Count(r => r.Dirty) + _pendingDeletes.Count; }
    }

    // ---- Record helpers: change memory, then name the record for the batch ----

    private static string RowKey(Guid id) => $"c:{id}";
    private static string DeleteKey(Guid id) => $"d:{id}";
    private static string MetaKey(string name) => $"m:{name}";

    private StoreRecord Put(CharacterRow row)
    {
        _rows[row.Id] = row;
        return new StoreRecord(RowKey(row.Id), JsonSerializer.Serialize(row, StoreJsonContext.Default.CharacterRow));
    }

    private StoreRecord RemoveRow(Guid id)
    {
        _rows.Remove(id);
        return new StoreRecord(RowKey(id), null);
    }

    private void Write(params IReadOnlyList<StoreRecord> batch)
    {
        if (batch.Count > 0) _journal.Write(batch);
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    // ---- ICharacterRepository ----

    public IReadOnlyList<CharacterSummary> List()
    {
        lock (_gate)
            return _rows.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new CharacterSummary(r.Id, r.Name, r.Description, r.UpdatedUtc)).ToList();
    }

    public Character? Load(Guid id)
    {
        CharacterRow? row;
        lock (_gate) row = _rows.GetValueOrDefault(id);
        if (row is null) return null;
        var character = CharacterJson.Deserialize(row.Data, row.SchemaVersion);
        character.Id = id;
        return character;
    }

    public void Save(Character character)
    {
        var name = CharacterSummaryText.DisplayName(character);
        var description = CharacterSummaryText.Describe(character);
        var data = CharacterJson.Serialize(character);
        var now = Now();
        lock (_gate)
        {
            var row = _rows.TryGetValue(character.Id, out var old)
                ? old with
                {
                    Name = name, Description = description, SchemaVersion = CharacterJson.CurrentSchemaVersion, Data = data,
                    UpdatedUtc = now, Dirty = true, LocalRevision = old.LocalRevision + 1,
                }
                : new CharacterRow(character.Id, name, description, CharacterJson.CurrentSchemaVersion, data, now, now, null, true, 1);
            Write(Put(row));
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(id, out var row)) return false;
            var batch = new List<StoreRecord>();
            // A synced character still exists in the cloud: queue a tombstone for the next sync pass.
            if (row.CloudRevision is long cloud)
            {
                _pendingDeletes[id] = cloud;
                batch.Add(new StoreRecord(DeleteKey(id), JsonSerializer.Serialize(cloud, StoreJsonContext.Default.Int64)));
            }
            batch.Add(RemoveRow(id));
            Write(batch);
            return true;
        }
    }

    // ---- ISyncLocalStore ----

    public IReadOnlyList<LocalChange> GetDirty()
    {
        lock (_gate)
            return _rows.Values.Where(r => r.Dirty)
                .Select(r => new LocalChange(r.Id, r.LocalRevision, r.CloudRevision, r.Name, r.Description, r.SchemaVersion, r.Data))
                .ToList();
    }

    public LocalState? GetState(Guid id)
    {
        lock (_gate) return _rows.TryGetValue(id, out var r) ? new LocalState(r.Dirty, r.LocalRevision) : null;
    }

    public void MarkPushed(Guid id, long localRevision, long cloudRevision)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(id, out var row))
                Write(Put(row with { CloudRevision = cloudRevision, Dirty = row.LocalRevision != localRevision }));
        }
    }

    public bool ApplyRemote(Character character, long cloudRevision, long? ifLocalRevision = null, bool force = false)
    {
        var name = CharacterSummaryText.DisplayName(character);
        var description = CharacterSummaryText.Describe(character);
        var data = CharacterJson.Serialize(character);
        var now = Now();
        lock (_gate)
        {
            if (_pendingDeletes.ContainsKey(character.Id)) return false;
            CharacterRow row;
            if (!_rows.TryGetValue(character.Id, out var old))
                row = new CharacterRow(character.Id, name, description, CharacterJson.CurrentSchemaVersion, data, now, now, cloudRevision, false, 0);
            else if (!old.Dirty || old.LocalRevision == ifLocalRevision || force)
                row = old with
                {
                    Name = name, Description = description, SchemaVersion = CharacterJson.CurrentSchemaVersion, Data = data,
                    UpdatedUtc = now, CloudRevision = cloudRevision, Dirty = false,
                };
            else return false;
            Write(Put(row));
            return true;
        }
    }

    public bool ApplyRemoteDelete(Guid id, bool force = false)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(id, out var row) || (row.Dirty && !force)) return false;
            Write(RemoveRow(id));
            return true;
        }
    }

    public void Rebase(Guid id, long? cloudRevision)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(id, out var row))
                Write(Put(row with { CloudRevision = cloudRevision, Dirty = true }));
        }
    }

    public IReadOnlyDictionary<Guid, long?> GetCloudRevisions()
    {
        lock (_gate) return _rows.Values.ToDictionary(r => r.Id, r => r.CloudRevision);
    }

    public IReadOnlyList<PendingDelete> GetPendingDeletes()
    {
        lock (_gate) return _pendingDeletes.Select(p => new PendingDelete(p.Key, p.Value)).ToList();
    }

    public void ClearPendingDelete(Guid id)
    {
        lock (_gate)
        {
            if (_pendingDeletes.Remove(id)) Write(new StoreRecord(DeleteKey(id), null));
        }
    }

    public SyncMeta GetSyncMeta()
    {
        lock (_gate)
            return new SyncMeta(_meta.GetValueOrDefault(AccountKey),
                _meta.TryGetValue(LastPullKey, out var lastPull)
                    ? DateTimeOffset.Parse(lastPull, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    : null);
    }

    public void SetSyncAccount(string account) => WriteMeta(AccountKey, account);

    public void SetLastPull(DateTimeOffset? at) =>
        WriteMeta(LastPullKey, at?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private void WriteMeta(string name, string? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                if (_meta.Remove(name)) Write(new StoreRecord(MetaKey(name), null));
                return;
            }
            _meta[name] = value;
            Write(new StoreRecord(MetaKey(name), JsonSerializer.Serialize(value, StoreJsonContext.Default.String)));
        }
    }

    public void ResetSyncState(bool markAllDirty)
    {
        lock (_gate)
        {
            var batch = _rows.Values.ToList().Select(r => Put(r with { CloudRevision = null, Dirty = markAllDirty })).ToList();
            batch.AddRange(_pendingDeletes.Keys.Select(id => new StoreRecord(DeleteKey(id), null)));
            _pendingDeletes.Clear();
            if (_meta.Remove(LastPullKey)) batch.Add(new StoreRecord(MetaKey(LastPullKey), null));
            Write(batch);
        }
    }

    public void ReassignAllIds()
    {
        lock (_gate)
        {
            var batch = new List<StoreRecord>();
            foreach (var row in _rows.Values.ToList())
            {
                batch.Add(RemoveRow(row.Id));
                batch.Add(Put(row with { Id = Guid.NewGuid() }));
            }
            Write(batch);
            _logger.LogInformation("Reassigned ids of {Count} characters", batch.Count / 2);
        }
    }
}
