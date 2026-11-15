using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hearthsheet.Infrastructure;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Per-device display preferences (ui.json). Kept apart from appsettings.user.json, which the user edits by hand.</summary>
public static class UiPreferences
{
    private sealed record Document(ThemeChoice Theme);

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The saved theme; anything missing, unreadable or unknown means <see cref="ThemeChoice.System"/>.</summary>
    public static ThemeChoice LoadTheme(string path)
    {
        try
        {
            var theme = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options)?.Theme ?? ThemeChoice.System;
            return Enum.IsDefined(theme) ? theme : ThemeChoice.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ThemeChoice.System;
        }
    }

    public static void SaveTheme(string path, ThemeChoice choice)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Document(choice), Options));
        File.Move(temp, path, overwrite: true);
    }
}
