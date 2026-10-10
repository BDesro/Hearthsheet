using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Hearthsheet.App.Mvvm;

/// <summary>
/// Keeps every table header readable: a column can't be squeezed narrower than its header's longest word,
/// so in a narrow window headers wrap between words (see the header style) instead of being cut off.
/// </summary>
public static class DataGridHeaders
{
    // SizeChanged rather than Loaded: WPF skips Loaded on elements without their own handler, and a header's
    // first layout raises SizeChanged anyway. Later resizes just re-check the minimum.
    public static void KeepWordsWhole() => EventManager.RegisterClassHandler(
        typeof(DataGridColumnHeader), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnHeaderSized));

    private static void OnHeaderSized(object sender, SizeChangedEventArgs e)
    {
        if (sender is not DataGridColumnHeader { Column: { } column, Content: string text } header) return;
        var typeface = new Typeface(header.FontFamily, header.FontStyle, header.FontWeight, header.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(header).PixelsPerDip;
        var longestWord = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty("").Max(word =>
            new FormattedText(word, CultureInfo.CurrentUICulture, header.FlowDirection, typeface, header.FontSize, Brushes.Black, dpi)
                .WidthIncludingTrailingWhitespace);
        // Padding, the right border and a little slack for rounding.
        var needed = Math.Ceiling(longestWord + header.Padding.Left + header.Padding.Right + header.BorderThickness.Right + 2);
        if (column.MinWidth < needed) column.MinWidth = needed;
    }
}
