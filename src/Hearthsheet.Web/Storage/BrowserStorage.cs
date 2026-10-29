using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthsheet.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web.Storage;

/// <summary>
/// The browser's storage: app data (sign-in secrets) in <c>hearthsheet-app</c>, and each account's characters only in
/// its own <c>hearthsheet-u-{userId}</c> database (spec 7a), so one person never loads another's records.
/// </summary>
public sealed class BrowserStorage
{
    private const string LastAccountKey = "hearthsheet.lastAccount";
    private readonly IJSInProcessObjectReference _js;
    private readonly ILoggerFactory _loggers;

    private BrowserStorage(IJSInProcessObjectReference js, ILoggerFactory loggers, BrowserSecretStore secrets)
    {
        _js = js;
        _loggers = loggers;
        Secrets = secrets;
        SessionSecrets = new SessionSecretStore(js);
    }

    /// <summary>"Stay signed in" store, for <c>SupabaseAuth</c>'s <c>secrets</c>.</summary>
    public BrowserSecretStore Secrets { get; }

    /// <summary>This tab's store, for <c>SupabaseAuth</c>'s <c>sessionSecrets</c>.</summary>
    public SessionSecretStore SessionSecrets { get; }

    /// <summary>The journal of the store last opened by <see cref="OpenAccountAsync"/>; its <c>Failed</c> is "Not saved on this device".</summary>
    public IndexedDbJournal? AccountJournal { get; private set; }

    /// <summary>Imports the JS module and loads <c>hearthsheet-app</c>.</summary>
    /// <exception cref="Hearthsheet.Cloud.SecretStoreException">IndexedDB could not be read.</exception>
    public static async Task<BrowserStorage> CreateAsync(IJSRuntime runtime, ILoggerFactory loggers)
    {
        var js = (IJSInProcessObjectReference)await runtime.InvokeAsync<IJSObjectReference>("import", "./js/hearthsheet.js");
        var journal = new IndexedDbJournal(js, "hearthsheet-app", loggers.CreateLogger<IndexedDbJournal>());
        return new BrowserStorage(js, loggers, await BrowserSecretStore.LoadAsync(js, journal));
    }

    /// <summary>Loads every record of the account's database before returning (spec 6).</summary>
    public async Task<MemoryCharacterStore> OpenAccountAsync(string userId)
    {
        var dbName = AccountDb(userId);
        var records = await _js.InvokeAsync<StoreRecord[]>("idbLoad", dbName);
        var journal = new IndexedDbJournal(_js, dbName, _loggers.CreateLogger<IndexedDbJournal>());
        var store = new MemoryCharacterStore(records, journal, _loggers.CreateLogger<MemoryCharacterStore>());
        AccountJournal = journal;
        return store;
    }

    /// <summary>
    /// Deletes the account's database from this browser. Call only after its shell is closed: a later write from the
    /// old store would recreate the database.
    /// </summary>
    public async Task ForgetAccountAsync(string userId)
    {
        var dbName = AccountDb(userId);
        if (AccountJournal?.DbName == dbName)
        {
            await AccountJournal.FlushAsync();
            AccountJournal = null;
        }
        await _js.InvokeVoidAsync("idbDelete", dbName);
    }

    /// <summary>Completes when every open journal has written what it holds (or its head batch has failed).</summary>
    public Task FlushAsync() => Task.WhenAll(Secrets.Journal.FlushAsync(), AccountJournal?.FlushAsync() ?? Task.CompletedTask);

    /// <summary>
    /// Stops every journal for good (see <c>OrderedJournal.Close</c>): for a tab that no longer holds the tab lock,
    /// whose in-memory store must never write again. Flush first.
    /// </summary>
    public void Close()
    {
        Secrets.Journal.Close();
        AccountJournal?.Close();
    }

    /// <summary>Asks the browser not to evict this site's storage (spec 8).</summary>
    public async Task<bool> PersistAsync() => await _js.InvokeAsync<bool>("persist");

    // ponytail: two strings until Task 10 moves LastAccount into Presentation.
    public (string UserId, string? Email)? ReadLastAccount()
    {
        var raw = _js.Invoke<string?>("local", LastAccountKey);
        if (raw is null) return null;
        try
        {
            var node = JsonNode.Parse(raw);
            var userId = node?["userId"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(userId)) return (userId, node?["email"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        _loggers.CreateLogger<BrowserStorage>().LogWarning("Ignored an unreadable {Key} in localStorage", LastAccountKey);
        return null;
    }

    public void WriteLastAccount(string userId, string? email) =>
        _js.InvokeVoid("setLocal", LastAccountKey, new JsonObject { ["userId"] = userId, ["email"] = email }.ToJsonString());

    public void ClearLastAccount() => _js.InvokeVoid("setLocal", LastAccountKey, null);

    /// <summary>The user id becomes part of a database name, so only a GUID (Supabase's user id) is accepted.</summary>
    private static string AccountDb(string userId) =>
        Guid.TryParse(userId, out var id) && id != Guid.Empty
            ? $"hearthsheet-u-{id:D}"
            : throw new ArgumentException("A user id must be a non-empty GUID.", nameof(userId));
}
