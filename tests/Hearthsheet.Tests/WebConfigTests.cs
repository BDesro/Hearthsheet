using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthsheet.Cloud;
using Hearthsheet.Core.Application;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Tests;

public class WebConfigTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hearthsheet.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    private static string Web(string relative) => Path.Combine(Root(), "src", "Hearthsheet.Web", relative);

    [Fact]
    public void ConnectSrc_MatchesTheConfiguredSupabaseUrl()
    {
        var url = JsonDocument.Parse(File.ReadAllText(Web("wwwroot/appsettings.json")))
            .RootElement.GetProperty("Sync").GetProperty("Url").GetString()!.TrimEnd('/');
        var connect = Regex.Match(File.ReadAllText(Web("wwwroot/_headers")), @"connect-src ([^;\r\n]+)").Groups[1].Value;
        Assert.Equal($"'self' {url}", connect.Trim());
    }

    [Theory]
    [InlineData("default-src 'self';")]
    [InlineData("script-src 'self' 'wasm-unsafe-eval';")]
    [InlineData("style-src 'self';")]
    [InlineData("img-src 'self' data: blob:;")]
    [InlineData("object-src 'none';")]
    [InlineData("base-uri 'self';")]
    [InlineData("form-action 'none';")]
    [InlineData("frame-ancestors 'none'")]
    [InlineData("Strict-Transport-Security:")]
    [InlineData("X-Content-Type-Options: nosniff")]
    [InlineData("Referrer-Policy: no-referrer")]
    [InlineData("Permissions-Policy:")]
    [InlineData("Cross-Origin-Opener-Policy: same-origin")]
    public void Headers_ContainEveryRequiredDirective(string expected) =>
        Assert.Contains(expected, File.ReadAllText(Web("wwwroot/_headers")));

    [Fact]
    public void WebSources_NeverUseMarkupString()
    {
        var files = Directory.EnumerateFiles(Web(""), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        Assert.DoesNotContain(files, f => File.ReadAllText(f).Contains("MarkupString"));
    }

    [Fact]
    public void IndexHtml_HasNoInlineScriptOrStyle()
    {
        var html = File.ReadAllText(Web("wwwroot/index.html"));
        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)", html);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\sstyle\s*=", html);
        Assert.Contains("<script src=\"js/hearthsheet.js\" type=\"module\">", html);
    }

    [Fact]
    public void ChangeTrackingSelfCheck_Passes() => Assert.True(ChangeTrackingSelfCheck.Passes());

    [Fact]
    public void BrowserLog_RedactsTokens()
    {
        var sink = new List<string>();
        using var provider = new MemoryLogProvider(sink.Add);
        provider.CreateLogger("t").LogInformation("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcDEF123 end");
        var snapshot = provider.Snapshot();
        Assert.Contains("[REDACTED]", snapshot);
        Assert.DoesNotContain("eyJhbGci", snapshot);
        Assert.DoesNotContain("eyJhbGci", string.Concat(sink));
    }

    [Fact]
    public void BrowserLog_KeepsOnlyTheLast2000Lines()
    {
        using var provider = new MemoryLogProvider();
        var log = provider.CreateLogger("t");
        for (var i = 0; i < 2005; i++) log.LogInformation("line {I}", i);
        var lines = provider.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2000, lines.Length);
        Assert.Contains("line 2004", lines[^1]);
    }
}
