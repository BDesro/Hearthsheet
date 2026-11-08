using Hearthsheet.Web;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

public class WebDialogsTests
{
    [Fact]
    public async Task Dialogs_ShowOneAtATime_InOrder()
    {
        var dialogs = new WebDialogs();
        var confirm = dialogs.ConfirmAsync("Delete?");
        var error = dialogs.ErrorAsync("Save failed");

        var first = dialogs.Current!;
        Assert.Equal(DialogKind.Confirm, first.Kind);
        dialogs.Complete(new DialogRequest(DialogKind.Info, "", ""), true); // not current: ignored
        Assert.Same(first, dialogs.Current);

        dialogs.Complete(first, null); // Escape
        Assert.False(await confirm);
        Assert.Equal(DialogKind.Error, dialogs.Current!.Kind);
        Assert.False(error.IsCompleted);

        dialogs.Complete(dialogs.Current, null);
        await error;
        Assert.Null(dialogs.Current);
    }

    [Fact]
    public async Task PlayDialogs_CarryTheirInput_AndReturnTheAnswer()
    {
        var dialogs = new WebDialogs();
        var c = CharacterFactory.Create(new NewCharacterOptions("Ada", "Wizard", 1, "", "", ""));

        IReadOnlyList<object> kinds = [EffectExpiry.Manual];
        var add = dialogs.QuickAddAsync("Add effect", kinds, EffectExpiry.Manual, withDescription: true);
        Assert.Equal(new QuickAddRequest(kinds, EffectExpiry.Manual, true), dialogs.Current!.Data);
        Assert.Equal("Add effect", dialogs.Current.Title);
        var result = new QuickAddResult("Bless", EffectExpiry.Manual, "");
        dialogs.Complete(dialogs.Current, result);
        Assert.Same(result, await add);

        var level = dialogs.LevelUpAsync(c, "Wizard");
        Assert.Equal(new LevelUpRequest(c, "Wizard"), dialogs.Current!.Data);
        dialogs.Complete(dialogs.Current, ("Wizard", new LevelUpOptions(Roll: 4)));
        Assert.Equal(("Wizard", new LevelUpOptions(Roll: 4)), await level);

        var rest = dialogs.ShortRestAsync(null!, c);
        dialogs.Complete(dialogs.Current!, null); // Escape
        Assert.False(await rest);
    }
}
