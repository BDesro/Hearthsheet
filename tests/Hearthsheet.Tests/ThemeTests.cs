using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hearthsheet.Tests;

/// <summary>The light and dark themes (desktop XAML and web CSS) stay in step and keep text readable.</summary>
public class ThemeTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hearthsheet.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    private static string AppDir => Path.Combine(Root(), "src", "Hearthsheet.App");

    private static Dictionary<string, string> XamlBrushes(string file)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(Path.Combine(AppDir, "Themes", file)).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush" && !((string)e.Attribute(x + "Key")!).StartsWith('{'))
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    /// <summary>WCAG 2 contrast ratio of two #RRGGBB colours.</summary>
    internal static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex[2..]; // #AARRGGBB: alpha ignored, theme brushes are opaque
            double Channel(int i)
            {
                var c = int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>Text-on-background pairs that must reach 4.5:1 in every theme.</summary>
    public static readonly (string Fore, string Back)[] TextPairs =
    [
        ("Ink", "Surface"), ("Ink", "Canvas"), ("Ink", "Sidebar"), ("Ink", "ReadOnlyFill"), ("Ink", "AccentSoft"),
        ("InkMuted", "Surface"), ("InkMuted", "Canvas"), ("InkMuted", "Sidebar"),
        ("Accent", "Surface"), ("Good", "Surface"), ("Danger", "Surface"), ("OnAccent", "Accent"),
    ];

    [Fact]
    public void ContrastHelper_MatchesKnownValues()
    {
        Assert.Equal(21, Contrast("#000000", "#FFFFFF"), 1);
        Assert.Equal(1, Contrast("#777777", "#777777"), 3);
    }

    [Fact]
    public void DesktopThemes_HaveTheSameKeys() =>
        Assert.Equal(XamlBrushes("Light.xaml").Keys.Order(), XamlBrushes("Dark.xaml").Keys.Order());

    [Theory]
    [InlineData("Light.xaml")]
    [InlineData("Dark.xaml")]
    public void DesktopThemes_TextPairsMeetAA(string file)
    {
        var brushes = XamlBrushes(file);
        var failures = TextPairs
            .Select(p => (p, ratio: Contrast(brushes[p.Fore], brushes[p.Back])))
            .Where(r => r.ratio < 4.5)
            .Select(r => $"{r.p.Fore} on {r.p.Back}: {r.ratio:0.00}");
        Assert.Empty(failures);
    }

    private static IEnumerable<string> AppXaml() =>
        Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void NoStaticReferencesToThemeBrushes()
    {
        var keys = string.Join("|", XamlBrushes("Light.xaml").Keys.Select(Regex.Escape));
        var pattern = new Regex($@"StaticResource ({keys})\}}");
        var hits = AppXaml().SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(t => pattern.IsMatch(t.line))
            .Select(t => $"{Path.GetFileName(t.f)}:{t.i + 1}");
        Assert.Empty(hits);
    }

    [Fact]
    public void NoHardCodedColoursOutsideThemes()
    {
        string[] allowed = ["Light.xaml", "Dark.xaml", "SheetStyles.xaml"];
        var pattern = new Regex("\"#[0-9A-Fa-f]{3,8}\"|=\"(White|Black)\"");
        var hits = AppXaml().Where(f => !allowed.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(t => pattern.IsMatch(t.line))
            .Select(t => $"{Path.GetFileName(t.f)}:{t.i + 1}");
        Assert.Empty(hits);
    }

    // ── Web (app.css) ──

    private static string WebFile(string relative) => Path.Combine(Root(), "src", "Hearthsheet.Web", "wwwroot", relative);
    /// <summary>app.css without comments, so a rule's selector text is just the selector.</summary>
    private static string Css => Regex.Replace(File.ReadAllText(WebFile("css/app.css")), @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>The custom properties declared in the first rule whose selector is exactly <paramref name="selector"/>.</summary>
    private static Dictionary<string, string> CssVars(string selector)
    {
        var rule = Regex.Matches(Css, @"([^{}]+)\{([^{}]*)\}").First(m => m.Groups[1].Value.Trim() == selector);
        return Regex.Matches(rule.Groups[2].Value, @"--([\w-]+)\s*:\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }

    private const string DarkSelector = ":root[data-theme=\"dark\"]";
    private const string LightSelector = ":root, .sheet-page";

    /// <summary><see cref="TextPairs"/> as CSS variable names.</summary>
    private static string CssName(string key) => key switch
    {
        "ReadOnlyFill" => "read-only",
        _ => Regex.Replace(key, "(?<!^)([A-Z])", "-$1").ToLowerInvariant(),
    };

    [Fact]
    public void WebThemes_HaveTheSameVariables() =>
        Assert.Equal(CssVars(LightSelector).Keys.Order(), CssVars(DarkSelector).Keys.Order());

    [Theory]
    [InlineData(LightSelector)]
    [InlineData(DarkSelector)]
    public void WebThemes_TextPairsMeetAA(string selector)
    {
        var vars = CssVars(selector);
        var failures = TextPairs
            .Select(p => (p, ratio: Contrast(vars[CssName(p.Fore)], vars[CssName(p.Back)])))
            .Where(r => r.ratio < 4.5)
            .Select(r => $"{r.p.Fore} on {r.p.Back}: {r.ratio:0.00}");
        Assert.Empty(failures);
    }

    /// <summary>
    /// Colours live in token rules (rules that only declare custom properties and color-scheme) so both themes cover
    /// them. Print styles are exempt: printouts are always light.
    /// </summary>
    [Fact]
    public void Css_HasNoHardCodedColoursOutsideTokens()
    {
        var css = Css;
        var print = css.IndexOf("@media print", StringComparison.Ordinal);
        if (print >= 0)
        {
            // Drop the print block (balanced braces).
            var depth = 0;
            var end = css.IndexOf('{', print);
            do
            {
                if (css[end] == '{') depth++;
                else if (css[end] == '}') depth--;
                end++;
            } while (depth > 0);
            css = css[..print] + css[end..];
        }
        var hits = Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}")
            .Where(m => !m.Groups[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(d => d.StartsWith("--", StringComparison.Ordinal) || d.StartsWith("color-scheme", StringComparison.Ordinal) || d.StartsWith("/*", StringComparison.Ordinal)))
            .Where(m => Regex.IsMatch(m.Groups[2].Value, @"#[0-9A-Fa-f]{3,8}\b|rgba?\((?!var\()"))
            .Select(m => m.Groups[1].Value.Trim());
        Assert.Empty(hits);
    }

    [Fact]
    public void IndexHtml_LoadsThemeScriptBeforeBlazor()
    {
        var html = File.ReadAllText(WebFile("index.html"));
        var theme = html.IndexOf("<script src=\"js/theme.js\"></script>", StringComparison.Ordinal);
        Assert.True(theme >= 0, "index.html must load js/theme.js");
        Assert.True(theme < html.IndexOf("blazor.webassembly.js", StringComparison.Ordinal), "theme.js must load before Blazor");
        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)[^>]*>", html); // the CSP forbids inline scripts
    }
}
