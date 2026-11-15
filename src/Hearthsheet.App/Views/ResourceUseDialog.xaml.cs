using System.Windows;

namespace Hearthsheet.App.Views;

public partial class ResourceUseDialog
{
    public ResourceUseDialog() => InitializeComponent();

    private void OnUse(object sender, RoutedEventArgs e) => DialogResult = true;
}
