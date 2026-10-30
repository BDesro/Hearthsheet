using Hearthsheet.Cloud;
using Hearthsheet.Core.Storage;
using Microsoft.JSInterop;

namespace Hearthsheet.Web.Storage;

/// <summary>
/// "Stay signed in" secrets in the <c>hearthsheet-app</c> IndexedDB database under <c>s:{key}</c> (value: the raw
/// secret). Reads come from memory, loaded once at startup; writes go through <see cref="Journal"/>, whose
/// <see cref="IndexedDbJournal.Failed"/> reports a write that did not reach the device.
/// </summary>
public sealed class BrowserSecretStore : ISecretStore
{
    private const string Prefix = "s:";
    private readonly Dictionary<string, string> _secrets;

    private BrowserSecretStore(Dictionary<string, string> secrets, IndexedDbJournal journal)
    {
        _secrets = secrets;
        Journal = journal;
    }

    public IndexedDbJournal Journal { get; }

    /// <exception cref="SecretStoreException">IndexedDB could not be read.</exception>
    public static async Task<BrowserSecretStore> LoadAsync(IJSObjectReference js, IndexedDbJournal journal)
    {
        StoreRecord[] records;
        try
        {
            records = await js.InvokeAsync<StoreRecord[]>("idbLoad", journal.DbName);
        }
        catch (JSException ex)
        {
            throw new SecretStoreException("Couldn't read saved sign-in from this browser.", ex);
        }
        var secrets = records.Where(r => r.Key.StartsWith(Prefix, StringComparison.Ordinal) && r.Json is not null)
            .ToDictionary(r => r.Key[Prefix.Length..], r => r.Json!);
        return new BrowserSecretStore(secrets, journal);
    }

    public string? Read(string key)
    {
        lock (_secrets) return _secrets.GetValueOrDefault(key);
    }

    public void Write(string key, string secret)
    {
        lock (_secrets)
        {
            _secrets[key] = secret;
            Journal.Write([new StoreRecord(Prefix + key, secret)]);
        }
    }

    public void Delete(string key)
    {
        lock (_secrets)
        {
            if (_secrets.Remove(key)) Journal.Write([new StoreRecord(Prefix + key, null)]);
        }
    }
}

/// <summary>Secrets for this tab only, in <c>sessionStorage</c> under <c>hearthsheet.{key}</c>.</summary>
public sealed class SessionSecretStore(IJSInProcessObjectReference js) : ISecretStore
{
    private const string Prefix = "hearthsheet.";

    public string? Read(string key) => Call(() => js.Invoke<string?>("session", Prefix + key));

    public void Write(string key, string secret) => Call(() => js.InvokeVoid("setSession", Prefix + key, secret));

    public void Delete(string key) => Call(() => js.InvokeVoid("setSession", Prefix + key, null));

    private static T Call<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (JSException ex)
        {
            throw new SecretStoreException("This browser's session storage isn't available.", ex);
        }
    }

    private static void Call(Action call) => Call(() => { call(); return 0; });
}
