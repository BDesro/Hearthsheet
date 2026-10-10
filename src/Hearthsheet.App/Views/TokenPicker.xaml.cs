using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.App.Views;

/// <summary>
/// Edits a comma-separated list (weapon types, weapon proficiencies). Ticking a suggestion adds or removes it,
/// which keeps spellings consistent; custom values can be typed in the box or added from the dropdown.
/// </summary>
public partial class TokenPicker : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TokenPicker),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IEnumerable<string>), typeof(TokenPicker),
        new PropertyMetadata(null, (d, _) => ((TokenPicker)d).Toggle.Visibility =
            ((TokenPicker)d).Options?.Any() == true ? Visibility.Visible : Visibility.Collapsed));

    public TokenPicker()
    {
        InitializeComponent();
        // Inside a DataGrid cell, clicks in the dropdown would bubble up to the cell, which takes focus mid-click and
        // swallows the checkbox's click. Taking the popup out of the tree ends its event route at the popup itself.
        ((Panel)Content).Children.Remove(DropDown);
        DropDown.PlacementTarget = this;
        DropDown.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = Toggle, Mode = BindingMode.TwoWay });
        DropContent.SetBinding(MinWidthProperty, new Binding(nameof(ActualWidth)) { Source = this });
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Suggestions; the dropdown button is hidden when there are none.</summary>
    public IEnumerable<string>? Options
    {
        get => (IEnumerable<string>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        BuildChoices();
        Toggle.IsHitTestVisible = false; // clicking the button again closes the popup instead of reopening it
    }

    /// <summary>The options plus any values already in the text that aren't among them, so they can be unticked too.</summary>
    private void BuildChoices()
    {
        var options = (Options ?? []).ToList();
        var extra = CharacterRules.SplitList(Text ?? "").Where(t => !options.Contains(t, StringComparer.OrdinalIgnoreCase));
        Choices.ItemsSource = options.Concat(extra).Select(o => new Choice(this, o)).ToList();
    }

    private void OnAddCustom(object sender, RoutedEventArgs e)
    {
        var value = Custom.Text.Trim().Trim(',', ';', '/').Trim();
        if (value.Length == 0) return;
        new Choice(this, value).IsChecked = true;
        Custom.Clear();
        BuildChoices();
    }

    private void OnCustomKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddCustom(sender, e);
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e) => Toggle.IsHitTestVisible = true;

    public sealed class Choice(TokenPicker owner, string name)
    {
        public string Name => name;

        public bool IsChecked
        {
            get => CharacterRules.SplitList(owner.Text ?? "").Contains(name, StringComparer.OrdinalIgnoreCase);
            set
            {
                var items = CharacterRules.SplitList(owner.Text ?? "")
                    .Where(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (value) items.Add(name);
                owner.Text = string.Join(", ", items);
            }
        }
    }
}
