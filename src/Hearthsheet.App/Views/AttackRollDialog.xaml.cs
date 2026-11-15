using System.Windows;
using System.Windows.Controls;
using Hearthsheet.Core.Rules;
using Hearthsheet.Presentation;

namespace Hearthsheet.App.Views;

public partial class AttackRollDialog
{
    public AttackRollDialog() => InitializeComponent();

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AttackRollViewModel vm && Enum.TryParse<RollMode>((string)((RadioButton)sender).Tag, out var mode))
            vm.Mode = mode;
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = true;
}
