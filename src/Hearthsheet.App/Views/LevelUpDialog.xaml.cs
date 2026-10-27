using System.Windows;
using System.Windows.Controls;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.App.Views;

public partial class LevelUpDialog
{
    private readonly Character _character;

    public LevelUpDialog(Character character, string? className)
    {
        InitializeComponent();
        _character = character;
        var existing = character.Identity.Classes.Select(e => e.Name).Where(n => n.Trim().Length > 0);
        ClassBox.ItemsSource = existing.Concat(SrdCatalog.Classes.Select(c => c.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ClassBox.Text = className ?? "";
        UpdateState();
    }

    public string ClassName { get; private set; } = "";
    public LevelUpOptions Options { get; private set; } = new();

    private string TypedName => ClassBox.Text.Trim();

    private bool IsCustom => TypedName.Length > 0 && SrdCatalog.FindClass(TypedName) is null
        && !_character.Identity.Classes.Any(e => string.Equals(e.Name.Trim(), TypedName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The class's hit die: an existing entry's, the catalog's, or the one picked for a custom class.</summary>
    private int Die =>
        _character.Identity.Classes.FirstOrDefault(e => string.Equals(e.Name.Trim(), TypedName, StringComparison.OrdinalIgnoreCase))?.HitDie
        ?? SrdCatalog.FindClass(TypedName)?.HitDie
        ?? DieBox.SelectedItem as int? ?? 8;

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateState();
    }

    private void UpdateState()
    {
        CustomPanel.Visibility = IsCustom ? Visibility.Visible : Visibility.Collapsed;
        var con = CharacterRules.AbilityModifier(_character, Ability.Constitution);
        AverageRadio.Content = $"Take average ({Math.Max(1, Die / 2 + 1 + con)})";
        var warnings = CharacterFactory.MulticlassWarnings(_character, TypedName);
        WarningText.Text = "Check: " + string.Join(" ", warnings);
        WarningText.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRoll(object sender, RoutedEventArgs e) =>
        RollBox.Text = Random.Shared.Next(1, Die + 1).ToString();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        int? roll = null;
        if (TypedName.Length == 0) { Fail("Pick or type a class.", ClassBox); return; }
        if (RollRadio.IsChecked == true)
        {
            if (!int.TryParse(RollBox.Text, out var r) || r < 1 || r > Die)
            {
                Fail($"Enter a roll from 1 to {Die}.", RollBox);
                return;
            }
            roll = r;
        }
        ClassName = TypedName;
        Options = IsCustom
            ? new LevelUpOptions(roll, Die, CasterBox.SelectedItem as CasterType?, AbilityBox.SelectedValue as Ability?)
            : new LevelUpOptions(roll);
        DialogResult = true;

        void Fail(string message, Control focus)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            focus.Focus();
        }
    }
}
