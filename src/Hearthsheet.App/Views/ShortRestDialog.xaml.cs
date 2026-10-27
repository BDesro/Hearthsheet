using System.Windows;
using Hearthsheet.App.ViewModels;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.App.Views;

public partial class ShortRestDialog
{
    private readonly PlayViewModel _play;
    private readonly Character _character;

    public ShortRestDialog(PlayViewModel play, Character character)
    {
        InitializeComponent();
        _play = play;
        _character = character;
        UpdateState();
    }

    private void OnSpend(object sender, RoutedEventArgs e)
    {
        var size = Size;
        if (!int.TryParse(RollBox.Text, out var roll) || roll < 1 || roll > size)
        {
            Feedback.Text = $"Enter a roll from 1 to {size}.";
            RollBox.Focus();
            return;
        }
        Spend(roll);
    }

    private void OnRoll(object sender, RoutedEventArgs e) =>
        Spend(Random.Shared.Next(1, Size + 1));

    // Task 5 adds a picker; until then the largest die with any left (or the largest) is spent.
    private int Size
    {
        get
        {
            var pools = CharacterRules.HitDicePools(_character);
            return (pools.FirstOrDefault(p => p.Remaining > 0) ?? pools[0]).Size;
        }
    }

    private void Spend(int roll)
    {
        var before = _character.Combat.CurrentHp;
        var size = Size;
        _play.SpendHitDie(size, roll);
        Feedback.Text = $"Rolled {roll} on d{size}: healed {_character.Combat.CurrentHp - before}.";
        RollBox.Clear();
        UpdateState();
    }

    private void UpdateState()
    {
        HpText.Text = $"HP {_character.Combat.CurrentHp} / {CharacterRules.MaxHitPoints(_character)}";
        var pools = CharacterRules.HitDicePools(_character);
        DiceText.Text = "Hit dice remaining: " + string.Join(", ", pools.Select(p => $"{p.Remaining} of {p.Total} d{p.Size}"));
        var canSpend = pools.Any(p => p.Remaining > 0) && !CharacterRules.IsDead(_character);
        SpendButton.IsEnabled = RollButton.IsEnabled = RollBox.IsEnabled = canSpend;
    }

    private void OnFinish(object sender, RoutedEventArgs e) => DialogResult = true;
}
