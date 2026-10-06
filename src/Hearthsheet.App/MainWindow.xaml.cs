using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Hearthsheet.App.ViewModels;

namespace Hearthsheet.App;

public partial class MainWindow
{
    public MainWindow() => InitializeComponent();

    // On some monitors (e.g. a scaled 4K display) hardware-rendered video freezes on its first frame while the audio plays.
    // Draw in software for the intro only, then go back to the default once the app shows.
    private static HwndTarget? Target(Window w) => (PresentationSource.FromVisual(w) as HwndSource)?.CompositionTarget;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (Target(this) is { } t) t.RenderMode = RenderMode.SoftwareOnly;
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
        if (!vm.OnClosing())
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

    private void OnIntroLoaded(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "tigrisoft-intro-short.mp4");
        if (!File.Exists(file)) { OnIntroDone(sender, e); return; }
        IntroVideo.Source = new Uri(file);
        IntroVideo.Pause(); // Manual mode only opens the media on Play/Pause.
    }

    // Playing before the media has opened can leave WPF audible but with the video stuck on its first frame.
    private void OnIntroOpened(object sender, RoutedEventArgs e)
    {
        FitIntro();
        IntroVideo.Play();
    }

    // Show the video 1:1 in physical pixels. Stretching it across a scaled 4K window makes software rendering choppy.
    private void FitIntro()
    {
        if (IntroVideo.NaturalVideoWidth == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        IntroVideo.Width = IntroVideo.NaturalVideoWidth / dpi.DpiScaleX;
        IntroVideo.Height = IntroVideo.NaturalVideoHeight / dpi.DpiScaleY;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitIntro();
    }

    private void OnIntroDown(object sender, MouseButtonEventArgs e) => OnIntroDone(sender, e);

    private bool _introDone;

    private void OnIntroDone(object sender, RoutedEventArgs e)
    {
        if (_introDone) return;
        _introDone = true;
        AppRoot.Visibility = Visibility.Visible;
        if (Target(this) is { } t) t.RenderMode = RenderMode.Default;

        // The title reads just "Hearthsheet" during the intro; hand it to the character title once the app shows.
        SetBinding(TitleProperty, new Binding("Current.Title")
        {
            StringFormat = "{0} — Hearthsheet", FallbackValue = "Hearthsheet", TargetNullValue = "Hearthsheet",
        });

        // Fade the intro out over the app instead of cutting to it.
        Intro.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(700)) { EasingFunction = new QuadraticEase() };
        fade.Completed += (_, _) =>
        {
            IntroVideo.Close();
            Intro.Visibility = Visibility.Collapsed;
        };
        Intro.BeginAnimation(OpacityProperty, fade);
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
