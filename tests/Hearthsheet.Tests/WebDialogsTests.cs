using Hearthsheet.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

public class WebDialogsTests
{
    [Fact]
    public async Task Dialogs_ShowOneAtATime_InOrder()
    {
        var dialogs = new WebDialogs(NullLogger<WebDialogs>.Instance);
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
}
