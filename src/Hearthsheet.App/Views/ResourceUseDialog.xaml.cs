using System.Windows;
using System.Windows.Controls;
using Hearthsheet.Presentation;

namespace Hearthsheet.App.Views;

public partial class ResourceUseDialog
{
    public ResourceUseDialog() => InitializeComponent();

    // The dice boxes are generated, so none can be named for FocusManager; the first one takes focus as it loads.
    private void OnBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext == (DataContext as ResourceUseViewModel)?.Boxes.FirstOrDefault()) box.Focus();
    }

    private void OnUse(object sender, RoutedEventArgs e) => DialogResult = true;
}
