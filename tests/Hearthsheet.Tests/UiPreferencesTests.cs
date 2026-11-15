using Hearthsheet.Infrastructure;

namespace Hearthsheet.Tests;

public sealed class UiPreferencesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hs-ui-" + Guid.NewGuid().ToString("N"));
    private string PathIn(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void LoadTheme_MissingFile_IsSystem() =>
        Assert.Equal(ThemeChoice.System, UiPreferences.LoadTheme(PathIn("ui.json")));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"Theme\":\"Purple\"}")]
    [InlineData("{\"Theme\":7}")]
    [InlineData("[]")]
    public void LoadTheme_CorruptOrUnknown_IsSystem(string contents)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn("ui.json"), contents);
        Assert.Equal(ThemeChoice.System, UiPreferences.LoadTheme(PathIn("ui.json")));
    }

    [Theory]
    [InlineData(ThemeChoice.System)]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    public void SaveThenLoad_RoundTrips(ThemeChoice choice)
    {
        UiPreferences.SaveTheme(PathIn("ui.json"), choice);
        Assert.Equal(choice, UiPreferences.LoadTheme(PathIn("ui.json")));
    }

    [Fact]
    public void SaveTheme_CreatesMissingDirectory()
    {
        var path = Path.Combine(_dir, "nested", "ui.json");
        UiPreferences.SaveTheme(path, ThemeChoice.Dark);
        Assert.True(File.Exists(path));
    }
}
