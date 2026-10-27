using System.Windows;
using System.Windows.Controls;
using Hearthsheet.App.ViewModels;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.App.Views;

public partial class ShortRestDialog
{
    private readonly PlayViewModel _play;
    private readonly Character _character;
    private bool _refreshing;

    public ShortRestDialog(PlayViewModel play, Character character)
    {
        InitializeComponent();
        _play = play;
        _character = character;
        UpdateState();
    }

    private void OnSpend(object sender, RoutedEventArgs e)
    {
        var size = Pool.Size;
        if (!int.TryParse(RollBox.Text, out var roll) || roll < 1 || roll > size)
        {
            Feedback.Text = $"Enter a roll from 1 to {size}.";
            RollBox.Focus();
            return;
        }
        Spend(size, roll);
    }

    private void OnRoll(object sender, RoutedEventArgs e)
    {
        var size = Pool.Size;
        Spend(size, Random.Shared.Next(1, size + 1));
    }

    private void OnPoolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) UpdateState();
    }

    /// <summary>The hit die pool picked in the combo box (the only one, when there's a single pool).</summary>
    private HitDicePool Pool
    {
        get
        {
            var pools = CharacterRules.HitDicePools(_character);
            return pools[Math.Clamp(PoolBox.SelectedIndex, 0, pools.Count - 1)];
        }
    }

    private void Spend(int size, int roll)
    {
        var before = _character.Combat.CurrentHp;
        _play.SpendHitDie(size, roll);
        Feedback.Text = $"Rolled {roll} on d{size}: healed {_character.Combat.CurrentHp - before}.";
        RollBox.Clear();
        UpdateState();
    }

    private void UpdateState()
    {
        HpText.Text = $"HP {_character.Combat.CurrentHp} / {CharacterRules.MaxHitPoints(_character)}";
        var pools = CharacterRules.HitDicePools(_character);
        // Rebuilding the list would reset the selection, so remember it (first run: the largest die with any left).
        var selected = PoolBox.SelectedIndex >= 0 ? PoolBox.SelectedIndex : Math.Max(0, pools.ToList().FindIndex(p => p.Remaining > 0));
        _refreshing = true;
        PoolBox.ItemsSource = pools.Select(p => $"d{p.Size} ({p.Remaining} left)").ToList();
        PoolBox.SelectedIndex = selected;
        _refreshing = false;
        PoolRow.Visibility = pools.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        // One pool keeps the original wording.
        DiceText.Text = pools is [var only]
            ? $"Hit dice remaining: {only.Remaining} of {only.Total} (d{only.Size})"
            : "Hit dice remaining: " + string.Join(", ", pools.Select(p => $"{p.Remaining} of {p.Total} d{p.Size}"));
        SpendButton.IsEnabled = RollButton.IsEnabled = RollBox.IsEnabled = Pool.Remaining > 0 && !CharacterRules.IsDead(_character);
    }

    private void OnFinish(object sender, RoutedEventArgs e) => DialogResult = true;
}
