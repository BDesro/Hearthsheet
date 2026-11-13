using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hearthsheet.Presentation;

namespace Hearthsheet.App.Views;

/// <summary>Shows the bundled player docs (<see cref="HelpDocs"/>). One non-modal window at a time.</summary>
public partial class HelpWindow
{
    private static HelpWindow? _instance;
    private readonly Stack<string> _history = new();
    private string _file = HelpDocs.Tutorial;

    private HelpWindow() => InitializeComponent();

    /// <summary>Shows <paramref name="file"/>, scrolled to <paramref name="anchor"/> if it names a heading.</summary>
    public static void Open(string file, string? anchor = null)
    {
        // Always a fresh window: one already open when a modal dialog's ShowDialog began stays disabled until that
        // dialog closes, while a window created now (e.g. from "Need help?") is usable on top of it.
        _instance?.Close();
        _instance = new HelpWindow { Owner = Application.Current.MainWindow };
        var window = _instance;
        window.Closed += (_, _) => { if (_instance == window) _instance = null; };
        _instance.Navigate(file, anchor);
        _instance.Show();
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void Navigate(string file, string? anchor)
    {
        _file = file;
        var blocks = HelpDocs.Load(file);
        Title = HelpDocs.Topics.FirstOrDefault(t => t.File == file)?.Title ?? "Hearthsheet help";
        Body.Children.Clear();
        foreach (var block in blocks) Body.Children.Add(Render(block));
        BackButton.IsEnabled = _history.Count > 0;

        var target = HelpDocs.FindAnchor(blocks, anchor);
        Scroller.ScrollToTop();
        if (target >= 0)
        {
            var element = (FrameworkElement)Body.Children[target];
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                Scroller.ScrollToVerticalOffset(element.TranslatePoint(new Point(), Body).Y));
        }
    }

    private UIElement Render(HelpBlock block)
    {
        if (block.Kind == HelpBlockKind.Image)
        {
            var image = new Image { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 14) };
            AutomationProperties.SetName(image, block.Runs[0].Text);
            if (HelpDocs.LoadImage(block.Image!) is { } bytes)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                image.Source = bitmap;
            }
            return image;
        }

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) };
        switch (block.Kind)
        {
            case HelpBlockKind.Heading:
                text.FontWeight = FontWeights.SemiBold;
                text.FontSize = block.Level switch { 1 => 24, 2 => 18, _ => 15 };
                text.Margin = new Thickness(0, block.Level == 1 ? 0 : 14, 0, 8);
                break;
            case HelpBlockKind.Bullet:
                text.Margin = new Thickness(14, 0, 0, 6);
                text.Inlines.Add(new Run("•  "));
                break;
        }
        foreach (var run in block.Runs)
        {
            Inline inline = run.Bold ? new Bold(new Run(run.Text)) : new Run(run.Text);
            if (run.Link is { } target)
            {
                var link = new Hyperlink(inline);
                link.Click += (_, _) => Follow(target);
                inline = link;
            }
            text.Inlines.Add(inline);
        }
        return text;
    }

    private void Follow(string target)
    {
        switch (HelpDocs.ResolveLink(target))
        {
            case { Url: { } url }:
                OpenUrl(url);
                break;
            case { } link:
                _history.Push(_file);
                Navigate(link.Doc ?? _file, link.Anchor);
                break;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_history.TryPop(out var previous)) Navigate(previous, null);
    }
}
