namespace Hearthsheet.Presentation;

/// <summary>The account last signed in on this browser, so an offline start still opens its store (spec 7a).</summary>
public sealed record LastAccount(string UserId, string? Email);

/// <summary>What the web opens at startup.</summary>
public abstract record AccountChoice
{
    private AccountChoice() { }

    /// <summary>Open this account's store; <see cref="SignedIn"/> false means "Signed out – sign in to resume sync".</summary>
    public sealed record Open(string UserId, bool SignedIn) : AccountChoice;

    /// <summary>Nothing to open: show only sign-in and sign-up.</summary>
    public sealed record SignInRequired : AccountChoice;
}

/// <summary>What signing out does after the final sync attempt.</summary>
public abstract record SignOutPlan
{
    private SignOutPlan() { }

    /// <summary>Everything is in the cloud: remove the account's store from this browser.</summary>
    public sealed record RemoveStore : SignOutPlan;

    /// <summary><see cref="Unsynced"/> changes would be lost: ask before going on.</summary>
    public sealed record AskUser(int Unsynced) : SignOutPlan;
}

/// <summary>The web's account rules (spec 7a), kept free of JS so they are unit-tested.</summary>
public static class WebStartup
{
    /// <summary>A restored session opens its own store; without one, the last account's store opens signed out.</summary>
    public static AccountChoice Choose(bool sessionRestored, string? sessionUserId, LastAccount? last) =>
        sessionRestored && sessionUserId is not null ? new AccountChoice.Open(sessionUserId, SignedIn: true)
        : last is not null ? new AccountChoice.Open(last.UserId, SignedIn: false)
        : new AccountChoice.SignInRequired();

    /// <param name="unsyncedAfterSync">The store's unsynced count after the sign-out's sync attempt.</param>
    public static SignOutPlan PlanSignOut(int unsyncedAfterSync) =>
        unsyncedAfterSync == 0 ? new SignOutPlan.RemoveStore() : new SignOutPlan.AskUser(unsyncedAfterSync);
}
