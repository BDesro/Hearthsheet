using System.Windows;
using Hearthsheet.App.ViewModels;
using Hearthsheet.App.Views;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Cloud;
using Hearthsheet.Presentation;
using Microsoft.Win32;

namespace Hearthsheet.App;

/// <summary>All modal UI launched from view models goes through here, keeping windows out of the view models.</summary>
public sealed class Dialogs : IDialogs
{
    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public Task<bool> ConfirmAsync(string message, string title = "Confirm") =>
        Task.FromResult(Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task ErrorAsync(string message)
    {
        Show(message, "Hearthsheet", MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public Task InfoAsync(string message)
    {
        Show(message, "Hearthsheet", MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image, MessageBoxResult.None)
            : MessageBox.Show(message, title, buttons, image, MessageBoxResult.None);

    public Task<NewCharacterOptions?> NewCharacterAsync()
    {
        var dialog = new NewCharacterDialog { Owner = Owner };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    public Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription)
    {
        var dialog = new QuickAddDialog(title, kinds, defaultKind, withDescription) { Owner = Owner };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    /// <param name="className">The class the dialog starts on; null starts blank.</param>
    public Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null)
    {
        var dialog = new LevelUpDialog(c, className) { Owner = Owner };
        return Task.FromResult<(string ClassName, LevelUpOptions Options)?>(dialog.ShowDialog() == true ? (dialog.ClassName, dialog.Options) : null);
    }

    /// <returns>True when the user chose to finish the short rest.</returns>
    public Task<bool> ShortRestAsync(PlayViewModel play, Character character) =>
        Task.FromResult(new ShortRestDialog(play, character) { Owner = Owner }.ShowDialog() == true);

    public void ShowUpdates(UpdateViewModel vm) =>
        new UpdateDialog { Owner = Owner, DataContext = vm }.ShowDialog();

    /// <returns>True when the user signed in (or created an account, or reset their password).</returns>
    public bool SignIn(SupabaseAuth auth) =>
        new AccountDialog(auth) { Owner = Owner }.ShowDialog() == true;

    /// <returns>True to upload this computer's characters, false to keep them local only, null to cancel (sign out).</returns>
    public bool? AccountSwitch() =>
        Show("This computer's characters were synced with another account.\n\n" +
             "Yes — upload them to this account\n" +
             "No — keep them on this computer only\n" +
             "Cancel — sign out\n\n" +
             "Nothing is deleted either way.",
             "Different account", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public string? OpenCharacterFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import character",
            Filter = $"Hearthsheet character (*{PortableCharacterFile.FileExtension})|*{PortableCharacterFile.FileExtension}|All files (*.*)|*.*",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveCharacterFile(string suggestedName)
    {
        var safeName = string.Concat(suggestedName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var dialog = new SaveFileDialog
        {
            Title = "Export character",
            FileName = safeName + PortableCharacterFile.FileExtension,
            DefaultExt = PortableCharacterFile.FileExtension,
            Filter = $"Hearthsheet character (*{PortableCharacterFile.FileExtension})|*{PortableCharacterFile.FileExtension}",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveZipFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export all characters",
            FileName = suggestedName + ".zip",
            DefaultExt = ".zip",
            Filter = "Zip archive (*.zip)|*.zip",
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
