using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

public class WebStartupTests
{
    private const string UserA = "11111111-1111-4111-8111-111111111111";
    private const string UserB = "22222222-2222-4222-8222-222222222222";

    [Fact]
    public void OpensTheSessionAccount_WhenRestored() =>
        Assert.Equal(new AccountChoice.Open(UserA, SignedIn: true),
            WebStartup.Choose(sessionRestored: true, UserA, new LastAccount(UserB, "b@example.com")));

    [Fact]
    public void OpensLastAccount_WhenSessionCannotBeRestored() =>
        Assert.Equal(new AccountChoice.Open(UserB, SignedIn: false),
            WebStartup.Choose(sessionRestored: false, null, new LastAccount(UserB, "b@example.com")));

    [Fact]
    public void RequiresSignIn_WithNoSessionAndNoLastAccount()
    {
        Assert.IsType<AccountChoice.SignInRequired>(WebStartup.Choose(sessionRestored: false, null, null));
        // A "restored" session without a user id can't name a store.
        Assert.IsType<AccountChoice.SignInRequired>(WebStartup.Choose(sessionRestored: true, null, null));
    }

    [Fact]
    public void SignOutRemovesStore_OnlyWhenNothingIsUnsynced()
    {
        Assert.IsType<SignOutPlan.RemoveStore>(WebStartup.PlanSignOut(0));
        Assert.Equal(new SignOutPlan.AskUser(1), WebStartup.PlanSignOut(1));
        Assert.Equal(new SignOutPlan.AskUser(7), WebStartup.PlanSignOut(7));
    }
}
