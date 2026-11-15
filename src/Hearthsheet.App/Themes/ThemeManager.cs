using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Hearthsheet.Infrastructure;
using Microsoft.Win32;

namespace Hearthsheet.App.Themes;

/// <summary>
/// Swaps Themes/Light.xaml and Themes/Dark.xaml in the application resources. Theme brushes are referenced with
/// DynamicResource, so open windows recolour immediately. "Match system" follows Windows' app mode while running.
/// </summary>
public static class ThemeManager
{
    private static bool s_hooked;

    public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Apply(ThemeChoice choice)
    {
        Hook();
        Choice = choice;
        IsDark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && WindowsUsesDarkApps());

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var index = dictionaries.ToList().FindIndex(d => d.Source?.OriginalString is { } s
            && (s.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) || s.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)));
        var theme = new ResourceDictionary { Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (index >= 0) dictionaries[index] = theme;
        else dictionaries.Insert(0, theme);

        foreach (Window window in Application.Current.Windows) SetTitleBar(window);
        Changed?.Invoke();
    }

    private static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        // New windows get the matching title bar as they load.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => SetTitleBar((Window)s)));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Choice == ThemeChoice.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
        };
    }

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark or light title bar to match; ignored where Windows doesn't support it.</summary>
    private static void SetTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        try { _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
