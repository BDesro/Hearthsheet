using Hearthsheet.Core.Application;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Storage;
using Hearthsheet.Presentation;
using Hearthsheet.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

[Collection("CommandRequery")]
public sealed class WebSyncTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ManualTime _time = new();
    private readonly FakeSession _session = new();
    private readonly FakeCloud _cloud;
    private readonly Machine _other;
    private readonly Machine _here;
    private readonly ShellViewModel _shell;
    private readonly WebSync _sync;

    public WebSyncTests()
    {
        _cloud = new FakeCloud(_time);
        _other = new Machine(_dir, "other", _cloud, _session, _time);
        _here = new Machine(_dir, "here", _cloud, _session, _time);
        var library = new CharacterLibrary(_here.Repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);
        _shell = new ShellViewModel(_here.Repo, library, RestService.CreateDefault(), new WebDialogs(NullLogger<WebDialogs>.Instance), NullLoggerFactory.Instance,
            _ => { }, _time, _here.Sync);
        _sync = new WebSync(_here.Sync, _shell, _session, new OrderedJournal(_ => Task.CompletedTask, NullLogger.Instance),
            NullLogger.Instance, a => a(), _time);
    }

    public void Dispose()
    {
        _sync.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public async Task RunAsync_SavesTheOpenEdit_PushesIt_AndShowsSynced()
    {
        _here.Add("Arannis");
        _shell.Start();
        _shell.Current!.Character.Identity.Name = "Arannis the Wise";

        await _sync.RunAsync();

        Assert.Equal("Arannis the Wise", Assert.Single(_cloud.Rows.Values).Name);
        Assert.StartsWith("Synced ", _sync.Status);
        Assert.False(_sync.IsBusy);
    }

    [Fact]
    public async Task RunAsync_AppliesDeferredChanges_ThroughTheShell()
    {
        var c = _other.Add("Arannis");
        await _other.SyncAsync();
        await _here.SyncAsync();
        _shell.Start();
        _other.Rename(c.Id, "Arannis the Wise");
        await _other.SyncAsync();

        await _sync.RunAsync();

        Assert.Equal("Updated from another device", _shell.SaveStatus);
        Assert.Equal("Arannis the Wise", _shell.Current?.Character.Identity.Name);
    }

    [Fact]
    public async Task RunAsync_WhileBusy_IsSkipped_AndKeepsTheStatus()
    {
        _cloud.Gate = new TaskCompletionSource();
        var gate = _cloud.Gate;
        var first = _sync.RunAsync();
        Assert.True(_sync.IsBusy);
        Assert.Equal("Syncing…", _sync.Status);

        var second = _sync.RunAsync();
        Assert.True(second.IsCompleted); // returned at once, no second pass queued
        Assert.True(_sync.IsBusy); // the first pass still owns the status
        Assert.Equal("Syncing…", _sync.Status);

        gate.SetResult();
        await first;
        Assert.StartsWith("Synced ", _sync.Status);
    }

    [Fact]
    public async Task Offline_ShowsOffline_AndKeepsTheChangeUnsynced()
    {
        var web = new WebMachine(new DictionaryJournal(), _cloud, _session, _time);
        var library = new CharacterLibrary(web.Repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);
        var shell = new ShellViewModel(web.Repo, library, RestService.CreateDefault(), new WebDialogs(NullLogger<WebDialogs>.Instance), NullLoggerFactory.Instance,
            _ => { }, _time, web.Sync);
        using var sync = new WebSync(web.Sync, shell, _session, new OrderedJournal(_ => Task.CompletedTask, NullLogger.Instance),
            NullLogger.Instance, a => a(), _time);
        web.Add("Arannis");
        _cloud.Offline = true;

        await sync.RunAsync();

        Assert.Equal("Offline", sync.Status);
        Assert.Equal("1 change not yet synced", SyncStatus.Unsynced(web.Repo.UnsyncedCount));

        _cloud.Offline = false;
        sync.Online();
        await Task.Yield();
        Assert.Equal(0, web.Repo.UnsyncedCount);
    }

    [Fact]
    public async Task SignedOut_ShowsTheSignInText_AndRunsNoPass()
    {
        _here.Add("Arannis");
        _session.UserId = null;

        await _sync.RunAsync();

        Assert.Equal("Signed out – sign in to resume sync", _sync.Status);
        Assert.Empty(_cloud.Rows);
    }

    [Fact]
    public void Tick_SyncsOnlyWhileVisible()
    {
        _here.Add("Arannis");

        _sync.IsVisible = false;
        _sync.Tick();
        Assert.Empty(_cloud.Rows);

        _sync.IsVisible = true;
        _sync.Tick(); // the fake cloud completes synchronously
        Assert.Single(_cloud.Rows);
    }

    [Fact]
    public async Task AfterDispose_NoPassRuns()
    {
        _here.Add("Arannis");
        _sync.Dispose();

        await _sync.RunAsync();

        Assert.Empty(_cloud.Rows);
    }
}
