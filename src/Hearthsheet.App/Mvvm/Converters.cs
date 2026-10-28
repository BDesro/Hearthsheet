using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.App.Mvvm;

/// <summary>Formats an int as a signed modifier ("+2", "−1").</summary>
public sealed class ModifierConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? CharacterRules.FormatModifier(i) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is true / non-null / non-zero / non-empty; "Invert" parameter flips it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "Invert") truthy = !truthy;
        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when an int value is at least the integer ConverterParameter (death-save pips).</summary>
public sealed class AtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && int.TryParse(parameter as string, out var n) && i >= n;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A class name for automation text, "Class" while it is blank.</summary>
public sealed class ClassNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s && s.Trim().Length > 0 ? s : "Class";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Per-spell save DC ("DC 15") from [spell, character]; empty when the spell's class has no ability.</summary>
public sealed class SpellDcConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [Spell s, Character c] && CharacterRules.SpellSaveDc(c, s) is { } dc ? $"DC {dc}" : "";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Lets a DataGrid column (which is outside the visual tree) bind to the view's DataContext.</summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));
    public object? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    protected override Freezable CreateInstanceCore() => new BindingProxy();
}

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => "—",
        Skill s => s.DisplayName(),
        Ability a => a.ToString(),
        RecoveryTiming.ShortRest => "Short or long rest",
        RecoveryTiming.LongRest => "Long rest",
        RecoveryTiming.Manual => "Manual",
        EffectExpiry.ShortRest => "Ends on any rest",
        EffectExpiry.LongRest => "Ends on long rest",
        EffectExpiry.Manual => "Until removed",
        ProficiencyLevel.Half => "Half proficiency",
        _ => SplitWords(value.ToString() ?? ""),
    };

    private static string SplitWords(string s) =>
        string.Concat(s.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString()));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
