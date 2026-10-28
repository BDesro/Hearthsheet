using System.Runtime.CompilerServices;
using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

[Collection("CommandRequery")]
public class CommandRequeryTests
{
    [Fact]
    public void Raise_NotifiesEveryLiveCommand()
    {
        var plain = new RelayCommand(() => { });
        var typed = new RelayCommand<int>(_ => { });
        int plainCount = 0, typedCount = 0;
        plain.CanExecuteChanged += (_, _) => plainCount++;
        typed.CanExecuteChanged += (_, _) => typedCount++;

        CommandRequery.Raise();

        Assert.Equal(1, plainCount);
        Assert.Equal(1, typedCount);
        GC.KeepAlive(plain);
        GC.KeepAlive(typed);
    }

    [Fact]
    public void Raise_DropsCollectedCommands()
    {
        CreateGarbageCommands(100);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        CommandRequery.Raise();

        Assert.True(CommandRequery.LiveCountForTests < 100);
    }

    [Fact]
    public void CanExecute_Typed_IgnoresOtherParameterTypes() =>
        Assert.False(new RelayCommand<int>(_ => { }).CanExecute("x"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateGarbageCommands(int count)
    {
        for (var i = 0; i < count; i++) _ = new RelayCommand(() => { });
    }
}
