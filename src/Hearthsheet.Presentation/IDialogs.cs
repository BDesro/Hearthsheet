using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Presentation;

/// <summary>The modal questions the character view models ask; each UI implements them with its own dialogs.</summary>
public interface IDialogs
{
    bool Confirm(string message, string title = "Confirm");
    void Info(string message);

    /// <param name="className">The class the dialog starts on; null starts blank.</param>
    (string ClassName, LevelUpOptions Options)? LevelUp(Character c, string? className = null);

    QuickAddResult? QuickAdd(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription);

    /// <returns>True when the user chose to finish the short rest.</returns>
    bool ShortRest(PlayViewModel play, Character character);
}
