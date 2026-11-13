using Hearthsheet.Presentation;

namespace Hearthsheet.Web;

/// <summary>
/// Which help doc is open, for the one <c>HelpViewer</c> host. Separate from <see cref="WebDialogs"/>'s queue so help
/// can open on top of a dialog that's waiting for an answer (the New character dialog's "Need help?").
/// </summary>
public sealed class WebHelp
{
    private readonly Stack<string> _history = new();

    public string? File { get; private set; }
    public string? Anchor { get; private set; }
    public bool CanGoBack => _history.Count > 0;

    /// <summary>Raised whenever the open doc changes or help closes.</summary>
    public event Action? Changed;

    /// <summary>Opens <paramref name="file"/> afresh, with no back history.</summary>
    public void Open(string file, string? anchor = null)
    {
        _history.Clear();
        Show(file, anchor);
    }

    /// <summary>Follows a link inside the open doc; Back returns from it.</summary>
    public void Follow(string? file, string? anchor)
    {
        if (File is not null) _history.Push(File);
        Show(file ?? File ?? HelpDocs.Tutorial, anchor);
    }

    public void Back()
    {
        if (_history.TryPop(out var previous)) Show(previous, null);
    }

    public void Close()
    {
        if (File is null) return;
        _history.Clear();
        File = null;
        Anchor = null;
        Changed?.Invoke();
    }

    private void Show(string file, string? anchor)
    {
        File = file;
        Anchor = anchor;
        Changed?.Invoke();
    }
}
