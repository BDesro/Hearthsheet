using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Hearthsheet.App.Views;

/// <summary>A themed stand-in for <see cref="MessageBox"/>: same buttons, default and Escape behaviour.</summary>
public partial class MessageDialog
{
    private MessageDialog(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = title;
        Message.Text = message;

        var (glyph, name, brush) = image switch
        {
            MessageBoxImage.Question => ("", "Question", "Accent"),
            MessageBoxImage.Error => ("", "Error", "Danger"),
            MessageBoxImage.Warning => ("", "Warning", "Danger"),
            MessageBoxImage.Information => ("", "Information", "Accent"),
            _ => ("", "", "Ink"),
        };
        Glyph.Text = glyph;
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        AutomationProperties.SetName(Glyph, name);
        if (glyph.Length == 0) Glyph.Visibility = Visibility.Collapsed;

        // Escape and the close box give what MessageBox gives: Cancel, else No, else OK.
        Result = buttons switch
        {
            MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.OK,
        };
        (string Text, MessageBoxResult Result)[] choices = buttons switch
        {
            MessageBoxButton.YesNo => [("_Yes", MessageBoxResult.Yes), ("_No", MessageBoxResult.No)],
            MessageBoxButton.YesNoCancel => [("_Yes", MessageBoxResult.Yes), ("_No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
            MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel)],
            _ => [("OK", MessageBoxResult.OK)],
        };
        for (var i = 0; i < choices.Length; i++)
        {
            var (text, result) = choices[i];
            var button = new Button { Content = text, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), IsDefault = i == 0, IsCancel = result == Result };
            if (i == 0) button.SetResourceReference(StyleProperty, "AccentButton");
            button.Click += (_, _) =>
            {
                Result = result;
                DialogResult = true;
            };
            Buttons.Children.Add(button);
            if (i == 0) Loaded += (_, _) => button.Focus();
        }
    }

    public MessageBoxResult Result { get; private set; }

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new MessageDialog(message, title, buttons, image);
        if (owner is not null) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Result;
    }
}
