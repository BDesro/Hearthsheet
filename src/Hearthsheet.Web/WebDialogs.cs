using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Presentation;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Web;

public enum DialogKind { Confirm, Info, Error, NewCharacter }

/// <summary>One question waiting for an answer. <see cref="WebDialogs.Complete"/> answers it.</summary>
public sealed class DialogRequest(DialogKind kind, string title, string message)
{
    public DialogKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Message { get; } = message;
    internal TaskCompletionSource<object?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The web's <see cref="IDialogs"/>. Requests queue up and the single <c>Modal</c> host shows one at a time, the next
/// once the previous is answered, so a fire-and-forget error can never open over a pending question.
/// </summary>
public sealed class WebDialogs(ILogger<WebDialogs> log) : IDialogs
{
    private readonly Queue<DialogRequest> _queue = new();

    /// <summary>The request on screen, or null.</summary>
    public DialogRequest? Current => _queue.TryPeek(out var r) ? r : null;

    /// <summary>Raised when <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Answers <paramref name="request"/> (null = cancelled) and shows the next one. Ignored unless it is current.</summary>
    public void Complete(DialogRequest request, object? answer)
    {
        if (Current != request) return;
        _queue.Dequeue();
        Changed?.Invoke();
        request.Answer.SetResult(answer);
    }

    private Task<object?> Ask(DialogKind kind, string title, string message)
    {
        var request = new DialogRequest(kind, title, message);
        _queue.Enqueue(request);
        if (_queue.Count == 1) Changed?.Invoke();
        return request.Answer.Task;
    }

    public async Task<bool> ConfirmAsync(string message, string title = "Confirm") =>
        await Ask(DialogKind.Confirm, title, message) is true;

    public Task InfoAsync(string message) => Ask(DialogKind.Info, "Hearthsheet", message);

    public Task ErrorAsync(string message) => Ask(DialogKind.Error, "Something went wrong", message);

    public async Task<NewCharacterOptions?> NewCharacterAsync() =>
        await Ask(DialogKind.NewCharacter, "New character", "") as NewCharacterOptions;

    // Phase 3 adds these; nothing calls them before then.
    public Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null)
    {
        log.LogWarning("Level up is not available in this preview");
        return Task.FromResult<(string, LevelUpOptions)?>(null);
    }

    public Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription)
    {
        log.LogWarning("Quick add is not available in this preview");
        return Task.FromResult<QuickAddResult?>(null);
    }

    public Task<bool> ShortRestAsync(PlayViewModel play, Character character)
    {
        log.LogWarning("Short rest is not available in this preview");
        return Task.FromResult(false);
    }
}
