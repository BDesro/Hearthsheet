using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Presentation;

/// <summary>The modal questions the character view models ask; each UI implements them with its own dialogs.</summary>
public interface IDialogs
{
    Task<bool> ConfirmAsync(string message, string title = "Confirm");
    Task InfoAsync(string message);
    Task ErrorAsync(string message);
    Task<NewCharacterOptions?> NewCharacterAsync();

    /// <param name="className">The class the dialog starts on; null starts blank.</param>
    Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null);

    Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription);

    /// <returns>True when the user chose to finish the short rest.</returns>
    Task<bool> ShortRestAsync(PlayViewModel play, Character character);
}
