using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Core.Serialization;

/// <summary>
/// The one character serialization used by both the local database and portable export files, so a
/// schema change needs exactly one migration path. Only concrete, known types are ever deserialized
/// (no type-name handling), which keeps untrusted input from choosing what gets instantiated.
/// </summary>
public static partial class CharacterJson
{
    /// <summary>
    /// Bump when the stored shape changes, and migrate the raw <see cref="JsonObject"/> from the old version in
    /// <see cref="Deserialize(JsonObject, int)"/> before it is materialized. Version 2 only added fields (weapons, wondrous
    /// items, feature modifiers), so version 1 reads as is. Version 3 moved class, subclass, level, hit die, hit dice left
    /// and spellcasting ability into <c>identity.classes[]</c> (multiclassing); older documents are migrated into one entry.
    /// The bump stops older apps from loading and dropping them.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32,
        WriteIndented = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        // Computed properties such as SpellSlotLevel.Available are read-only and are skipped on write.
        IgnoreReadOnlyProperties = true,
    };

    public static JsonObject ToJsonObject(Character c) =>
        JsonSerializer.SerializeToNode(c, Options)!.AsObject();

    public static string Serialize(Character c) => JsonSerializer.Serialize(c, Options);

    /// <summary>Materializes <paramref name="node"/>, which was saved at <paramref name="schemaVersion"/>.</summary>
    public static Character Deserialize(JsonObject node, int schemaVersion)
    {
        if (schemaVersion < 1)
            throw new CharacterFormatException($"Invalid character schema version {schemaVersion}.");
        if (schemaVersion > CurrentSchemaVersion)
            throw new CharacterFormatException(
                $"This character was saved by a newer version of the application (schema {schemaVersion}; this version supports up to {CurrentSchemaVersion}). Update the application to open it.");
        if (schemaVersion < 3) MigrateToClasses(node);
        var character = node.Deserialize<Character>(Options)
                        ?? throw new CharacterFormatException("Character data is empty.");
        character.Normalize();
        return character;
    }

    [GeneratedRegex(@"^Pact Magic slots \(level (\d)\)")]
    private static partial Regex PactResource();

    private static JsonObject Section(JsonObject parent, string key) =>
        parent[key] switch
        {
            JsonObject o => o,
            null => (JsonObject)(parent[key] = new JsonObject()),
            _ => throw new CharacterFormatException($"Character data is malformed: \"{key}\" must be an object."),
        };

    /// <summary>Schema 2 to 3: folds the single-class fields into <c>identity.classes[0]</c>.</summary>
    private static void MigrateToClasses(JsonObject node)
    {
        var identity = Section(node, "identity");
        var combat = Section(node, "combat");
        var spellcasting = Section(node, "spellcasting");

        T Take<T>(JsonObject o, string key, T fallback)
        {
            var value = o[key];
            o.Remove(key);
            if (value is null) return fallback;
            try { return value.GetValue<T>(); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                throw new CharacterFormatException($"Character data is malformed: \"{key}\" has the wrong type.", ex);
            }
        }

        var name = Take(identity, "className", "");
        var subclass = Take(identity, "subclass", "");
        var level = Take(identity, "level", 1);
        var hitDie = Take(combat, "hitDieSize", 8);
        var remaining = Take(combat, "hitDiceRemaining", level);
        var ability = Take<string?>(spellcasting, "ability", null);

        var caster = SrdCatalog.FindClass(name)?.Caster.ToString() ?? (ability is null ? "None" : "Full");
        if (caster == "None" && ClassLevel.IsThirdCasterSubclass(subclass))
        {
            caster = "Third";
            ability ??= "Intelligence";
        }
        identity["classes"] = new JsonArray(new JsonObject
        {
            ["name"] = name,
            ["subclass"] = subclass,
            ["level"] = level,
            ["hitDie"] = hitDie,
            ["hitDiceRemaining"] = remaining,
            ["caster"] = caster,
            ["spellcastingAbility"] = ability,
        });

        // Pact Magic used to be a resource; it is now spellcasting.pactSlots.
        if (node["resources"] is JsonArray resources)
        {
            foreach (var r in resources.OfType<JsonObject>())
            {
                if (r["name"] is not JsonValue nv || !nv.TryGetValue<string>(out var rname)) continue;
                var m = PactResource().Match(rname);
                if (!m.Success) continue;
                var max = Take(r, "maximum", 1);
                var cur = Take(r, "current", max);
                spellcasting["pactSlots"] = new JsonObject
                {
                    ["level"] = int.Parse(m.Groups[1].Value),
                    ["maximum"] = max,
                    ["expended"] = max - cur,
                };
                resources.Remove(r);
                break;
            }
        }
    }

    public static Character Deserialize(string json, int schemaVersion)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            throw new CharacterFormatException("Character data is not valid JSON.", ex);
        }
        if (node is not JsonObject obj) throw new CharacterFormatException("Character data must be a JSON object.");
        try
        {
            return Deserialize(obj, schemaVersion);
        }
        catch (JsonException ex)
        {
            throw new CharacterFormatException($"Character data is malformed: {ex.Message}", ex);
        }
    }

    public static Character Clone(Character c) => Deserialize(Serialize(c), CurrentSchemaVersion);
}

public sealed class CharacterFormatException(string message, Exception? inner = null) : Exception(message, inner);
