using Hearthsheet.Cloud;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Storage;
using Hearthsheet.Core.Sync;
using Hearthsheet.Web.Storage;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Web;

/// <summary>
/// One account's store opened in this tab, with the sync and library built on it (spec 7a: one store per account).
/// Closing it stops its journal for good, so a store that is no longer shown can never write again.
/// </summary>
public sealed class AccountSession
{
    private AccountSession(string userId, string? email, MemoryCharacterStore store, IndexedDbJournal journal,
        SupabaseAuth auth, CloudSync sync, CharacterLibrary library)
    {
        (UserId, Email, Store, Journal, Auth, Sync, Library) = (userId, email, store, journal, auth, sync, library);
    }

    public string UserId { get; }
    public string? Email { get; set; }
    public MemoryCharacterStore Store { get; }
    /// <summary>The store's journal; its <c>Failed</c> is "Not saved on this device".</summary>
    public IndexedDbJournal Journal { get; }
    public SupabaseAuth Auth { get; }
    public CloudSync Sync { get; }
    public CharacterLibrary Library { get; }

    /// <summary>Saves what the UI holds before the store is synced or closed. Task 11 sets it to the shell's <c>Save()</c>.</summary>
    public Action Save { get; set; } = () => { };

    /// <summary>The character open in the UI, for <see cref="CloudSync.RunAsync"/>. Task 11 sets it from the shell.</summary>
    public Func<Guid?> OpenCharacter { get; set; } = () => null;

    /// <summary>When passes run; set by the shell while it is shown (Task 12).</summary>
    public WebSync? WebSync { get; set; }

    public static async Task<AccountSession> OpenAsync(string userId, string? email, BrowserStorage storage,
        SupabaseHttp http, SupabaseAuth auth, ILoggerFactory loggers)
    {
        var store = await storage.OpenAccountAsync(userId);
        var sync = new CloudSync(store, store, new SupabaseCharacterStore(http, auth), auth, loggers.CreateLogger<CloudSync>());
        var library = new CharacterLibrary(store, new PortableCharacterFile(), AppVersion, loggers.CreateLogger<CharacterLibrary>());
        return new AccountSession(userId, email, store, storage.AccountJournal!, auth, sync, library);
    }

    /// <summary>The app version written into exports.</summary>
    public static string AppVersion { get; } =
        typeof(AccountSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Saves, then waits until the journal has written everything (or its head batch has failed).</summary>
    public Task SaveAndFlushAsync()
    {
        Save();
        return Journal.FlushAsync();
    }

    /// <summary>Stops the journal for good. Call after <see cref="SaveAndFlushAsync"/>.</summary>
    public void Close() => Journal.Close();
}
