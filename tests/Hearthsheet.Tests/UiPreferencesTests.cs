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
    public void Load_MissingFile_IsDefaults() =>
        Assert.Equal(new UiSettings(ThemeChoice.System, IntroSound: false), UiPreferences.Load(PathIn("ui.json")));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"Theme\":\"Purple\"}")]
    [InlineData("{\"Theme\":7}")]
    [InlineData("[]")]
    public void Load_CorruptOrUnknown_IsSystem(string contents)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn("ui.json"), contents);
        Assert.Equal(ThemeChoice.System, UiPreferences.Load(PathIn("ui.json")).Theme);
    }

    [Fact]
    public void Load_ThemeOnlyFile_KeepsThemeAndSoundOff()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn("ui.json"), "{\"Theme\":\"Dark\"}");
        Assert.Equal(new UiSettings(ThemeChoice.Dark, IntroSound: false), UiPreferences.Load(PathIn("ui.json")));
    }

    [Theory]
    [InlineData(ThemeChoice.System, false)]
    [InlineData(ThemeChoice.Light, true)]
    [InlineData(ThemeChoice.Dark, false)]
    public void SaveThenLoad_RoundTrips(ThemeChoice theme, bool sound)
    {
        UiPreferences.Save(PathIn("ui.json"), new UiSettings(theme, sound));
        Assert.Equal(new UiSettings(theme, sound), UiPreferences.Load(PathIn("ui.json")));
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var path = Path.Combine(_dir, "nested", "ui.json");
        UiPreferences.Save(path, new UiSettings(ThemeChoice.Dark));
        Assert.True(File.Exists(path));
    }
}
