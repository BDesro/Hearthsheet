using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hearthsheet.Infrastructure;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Per-device display preferences. <paramref name="IntroSound"/>: play the launch intro's audio (off by default).</summary>
public sealed record UiSettings(ThemeChoice Theme = ThemeChoice.System, bool IntroSound = false);

/// <summary>Reads and writes <see cref="UiSettings"/> (ui.json). Kept apart from appsettings.user.json, which the user edits by hand.</summary>
public static class UiPreferences
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The saved settings; a missing or unreadable file means the defaults, an unknown theme means <see cref="ThemeChoice.System"/>.</summary>
    public static UiSettings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path), Options) ?? new();
            return Enum.IsDefined(settings.Theme) ? settings : settings with { Theme = ThemeChoice.System };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public static void Save(string path, UiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
