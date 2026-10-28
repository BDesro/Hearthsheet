using Hearthsheet.Core.Sync;

namespace Hearthsheet.Presentation;

/// <summary>The status-bar texts for a finished sync pass, shared so desktop and web say the same thing.</summary>
public static class SyncStatus
{
    /// <returns>The new status, or null to keep the current one (<see cref="SyncOutcome.Busy"/>: another pass is running).
    /// <see cref="SyncOutcome.AccountMismatch"/> needs the caller's account-switch prompt and is not handled here.</returns>
    public static string? For(SyncResult result, DateTimeOffset localNow) => result.Outcome switch
    {
        SyncOutcome.Synced => $"Synced {localNow:HH:mm}" + Details(result),
        SyncOutcome.Offline => "Offline",
        SyncOutcome.Unavailable => "Cloud unavailable",
        SyncOutcome.SignedOut => "Signed out – sign in to resume sync",
        SyncOutcome.NotSignedIn => "Not signed in",
        SyncOutcome.Busy => null,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Handled by the caller"),
    };

    public static string Unsynced(int count) => count switch
    {
        0 => "",
        1 => "1 change not yet synced",
        _ => $"{count} changes not yet synced",
    };

    private static string Details(SyncResult result) =>
        (result.ConflictCopies > 0 ? $" · {result.ConflictCopies} conflict {(result.ConflictCopies == 1 ? "copy" : "copies")} kept" : "")
        + (result.Warnings.Count > 0 ? $" · {result.Warnings.Count} not synced (see log)" : "");
}
