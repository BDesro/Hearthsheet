using System.Windows;
using System.Windows.Controls;

namespace Hearthsheet.App.Views;

public partial class DetailsView
{
    public DetailsView() => InitializeComponent();

    /// <summary>Template-column editors don't take focus on their own the way built-in columns do.</summary>
    private void OnCellEditorLoaded(object sender, RoutedEventArgs e)
    {
        var editor = (Control)sender;
        editor.Focus();
        if (editor is TextBox box) box.SelectAll();
    }
}
