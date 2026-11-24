using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Hearthsheet.App.ViewModels;

namespace Hearthsheet.App;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // Toolbar labels collapse to icons on narrow windows (tooltips and automation names keep the words).
        SizeChanged += (_, _) => Toolbar.Tag = ActualWidth < 1100 ? "Compact" : null;
    }

    /// <summary>Help ▾ and ⋯ More: open the button's menu below it on a normal click.</summary>
    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private bool _readyToClose;
    private bool _finalSyncing;

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose || Vm is not { } vm) return;
        if (_finalSyncing)
        {
            e.Cancel = true;
            return;
        }
        // WPF dialogs complete synchronously, so this await finishes before Closing reads e.Cancel.
        if (!await vm.OnClosingAsync())
        {
            e.Cancel = true;
            return;
        }
        if (!vm.Sync.IsSignedIn) return;

        // One last bounded sync pass, then close for real.
        e.Cancel = true;
        IsEnabled = false;
        _finalSyncing = true;
        try
        {
            await vm.Sync.FinalSyncAsync();
        }
        finally
        {
            _readyToClose = true;
            Close();
        }
    }

    // ---- Logo intro: video → native logo → the tiger flies into the toolbar while the black fades over the app.

    private bool _introDone;
    private VideoFrames? _video;
    private MediaPlayer? _sound;

    private void OnIntroLoaded(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "tigrisoft-intro-app.mp4");
        // Reduced motion (or no video): skip it and show the static logo for a moment instead.
        if (!SystemParameters.ClientAreaAnimation || !File.Exists(file)) { Handoff(TimeSpan.FromSeconds(1)); return; }
        if (Vm?.IntroSound == true)
        {
            _sound = new MediaPlayer(); // audio only: the picture comes from VideoFrames
            _sound.Open(new Uri(file));
        }
        _video = new VideoFrames(file, IntroVideo);
        _video.Started += () => _sound?.Play();
        // The app cut ends on a still that the native logo underneath matches exactly, so hiding the video changes nothing.
        _video.Ended += () => Handoff(TimeSpan.Zero);
        _video.Failed += _ => Handoff(TimeSpan.FromSeconds(1));
        _video.Start();
    }

    private void OnIntroDown(object sender, MouseButtonEventArgs e) => Handoff(TimeSpan.Zero);

    private void OnIntroKey(object sender, KeyEventArgs e)
    {
        if (_introDone) return;
        e.Handled = true;
        Handoff(TimeSpan.Zero);
    }

    /// <summary>Swap the video for the native logo, show the app, then turn the logo into the toolbar's after <paramref name="hold"/>.</summary>
    private void Handoff(TimeSpan hold)
    {
        if (_introDone) return;
        _introDone = true;
        _video?.Dispose();
        IntroVideo.Visibility = Visibility.Collapsed;
        IntroTiger.Opacity = IntroWordmark.Opacity = 1;
        Intro.IsHitTestVisible = false;
        AppRoot.Visibility = Visibility.Visible;

        // The title reads just "Hearthsheet" during the intro; hand it to the character title once the app shows.
        SetBinding(TitleProperty, new Binding("Shell.Current.Title")
        {
            StringFormat = "{0} — Hearthsheet", FallbackValue = "Hearthsheet", TargetNullValue = "Hearthsheet",
        });

        void Done()
        {
            _sound?.Close();
            HeaderLogo.Opacity = 1;
            Intro.Visibility = Visibility.Collapsed;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            Fade(Intro, hold, 400, Done); // no movement: just fade over the app
            return;
        }

        // Map the tiger's on-screen rect onto the header logo's. The transform acts before the stage's Uniform scale k.
        UpdateLayout();
        var from = IntroTiger.TransformToAncestor(this).TransformBounds(new Rect(IntroTiger.RenderSize));
        var to = HeaderLogo.TransformToAncestor(this).TransformBounds(new Rect(HeaderLogo.RenderSize));
        var k = from.Width / IntroTiger.ActualWidth;
        var scale = new ScaleTransform();
        var move = new TranslateTransform();
        IntroTiger.RenderTransform = new TransformGroup { Children = { scale, move } };

        var fly = TimeSpan.FromMilliseconds(850);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimation To(double value) => new(value, fly) { BeginTime = hold, EasingFunction = ease };
        var last = To(to.Width / from.Width);
        last.Completed += (_, _) => Done();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(to.Width / from.Width));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, last);
        move.BeginAnimation(TranslateTransform.XProperty, To((to.X - from.X) / k));
        move.BeginAnimation(TranslateTransform.YProperty, To((to.Y - from.Y) / k));
        Fade(IntroWordmark, hold, 250);
        Fade(IntroBlack, hold + TimeSpan.FromMilliseconds(150), 650);
    }

    private static void Fade(UIElement element, TimeSpan begin, int ms, Action? completed = null)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = new QuadraticEase() };
        if (completed is not null) fade.Completed += (_, _) => completed();
        element.BeginAnimation(OpacityProperty, fade);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void CanPrint(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Vm?.HasCharacter == true;

    private void OnPrint(object sender, ExecutedRoutedEventArgs e)
    {
        if (Vm is null) return;
        // The sheet must be laid out to print, so switch to it first.
        Vm.SelectedView = 0;
        Dispatcher.BeginInvoke(Sheet.Print, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
