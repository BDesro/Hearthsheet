using Hearthsheet.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web.Storage;

/// <summary>An <see cref="OrderedJournal"/> that writes each batch to one IndexedDB database in one transaction.</summary>
public sealed class IndexedDbJournal(IJSObjectReference js, string dbName, ILogger logger)
    : OrderedJournal(batch => js.InvokeVoidAsync("idbWrite", dbName, batch).AsTask(), logger)
{
    public string DbName { get; } = dbName;
}
