using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Presentation;

namespace Hearthsheet.Web;

public enum DialogKind { Confirm, Info, Error, NewCharacter, QuickAdd, ShortRest, LevelUp }

/// <summary>What the Quick add dialog offers: a type list (or none) and whether a description is asked for.</summary>
public sealed record QuickAddRequest(IReadOnlyList<object>? Kinds, object? DefaultKind, bool WithDescription);

public sealed record ShortRestRequest(PlayViewModel Play, Character Character);

/// <param name="ClassName">The class the dialog starts on; null starts blank.</param>
public sealed record LevelUpRequest(Character Character, string? ClassName);

/// <summary>One question waiting for an answer. <see cref="WebDialogs.Complete"/> answers it.</summary>
public sealed class DialogRequest(DialogKind kind, string title, string message, object? data = null)
{
    public DialogKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Message { get; } = message;
    /// <summary>The dialog's input: a <see cref="QuickAddRequest"/>, <see cref="ShortRestRequest"/> or <see cref="LevelUpRequest"/>.</summary>
    public object? Data { get; } = data;
    internal TaskCompletionSource<object?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// The web's <see cref="IDialogs"/>. Requests queue up and the single <c>Modal</c> host shows one at a time, the next
/// once the previous is answered, so a fire-and-forget error can never open over a pending question.
/// </summary>
public sealed class WebDialogs : IDialogs
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

    private Task<object?> Ask(DialogKind kind, string title, string message, object? data = null)
    {
        var request = new DialogRequest(kind, title, message, data);
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

    public async Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null) =>
        await Ask(DialogKind.LevelUp, "Level up", "", new LevelUpRequest(c, className)) as (string, LevelUpOptions)?;

    public async Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription) =>
        await Ask(DialogKind.QuickAdd, title, "", new QuickAddRequest(kinds, defaultKind, withDescription)) as QuickAddResult;

    public async Task<bool> ShortRestAsync(PlayViewModel play, Character character) =>
        await Ask(DialogKind.ShortRest, "Short rest", "", new ShortRestRequest(play, character)) is true;
}
