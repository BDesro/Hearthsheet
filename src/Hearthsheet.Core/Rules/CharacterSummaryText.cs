using Hearthsheet.Core.Domain;

namespace Hearthsheet.Core.Rules;

/// <summary>The name and one-line description a store lists a character under.</summary>
public static class CharacterSummaryText
{
    public static string DisplayName(Character c) =>
        string.IsNullOrWhiteSpace(c.Identity.Name) ? "Unnamed character" : c.Identity.Name;

    public static string Describe(Character c)
    {
        var parts = new[] { c.Identity.Species, CharacterRules.ClassAndLevelText(c) }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" · ", parts);
    }
}
