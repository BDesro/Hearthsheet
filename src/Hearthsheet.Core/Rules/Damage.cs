using System.Text.RegularExpressions;

namespace Hearthsheet.Core.Rules;

public sealed record DiceTerm(int Count, int Sides);

/// <summary>One damage type's share of an attack or spell. Type "" means untyped.</summary>
public sealed record DamagePart(string Type, IReadOnlyList<DiceTerm> Dice, int Flat);

/// <summary>Parses free-text damage such as "1d10 + 3 piercing + 1d6 cold" into typed parts.</summary>
public static class DamageText
{
    private static readonly int[] ValidSides = [2, 3, 4, 6, 8, 10, 12, 20, 100];

    private static readonly Regex Term = new(
        @"^\s*(?:(?<n>[0-9]*)d(?<s>[0-9]+)|(?<flat>[0-9]+))\s*(?<type>[A-Za-z]+)?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>The parts, an empty list for blank text, or null when the text isn't plain dice-and-types.</summary>
    public static IReadOnlyList<DamagePart>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // Split keeps the separators: term, sign, term, sign, ...
        var tokens = Regex.Split(text, @"([+\-−])");
        var parts = new List<(string Type, List<DiceTerm> Dice, int Flat)>();
        var dice = new List<DiceTerm>();
        var flat = 0;
        var pending = false;

        for (var i = 0; i < tokens.Length; i += 2)
        {
            var negative = i > 0 && tokens[i - 1] != "+";
            var m = Term.Match(tokens[i]);
            if (!m.Success) return null;

            if (m.Groups["s"].Success)
            {
                if (negative) return null;
                var count = 1;
                if (m.Groups["n"].Length > 0 && !int.TryParse(m.Groups["n"].Value, out count)) return null;
                if (!int.TryParse(m.Groups["s"].Value, out var sides) || !ValidSides.Contains(sides)) return null;
                if (count is < 1 or > 100) return null;
                dice.Add(new DiceTerm(count, sides));
            }
            else
            {
                if (!int.TryParse(m.Groups["flat"].Value, out var n)) return null;
                flat += negative ? -n : n;
            }
            pending = true;

            if (m.Groups["type"].Success)
            {
                Close(m.Groups["type"].Value);
            }
        }

        if (pending) Close("");
        return parts.Select(p => new DamagePart(p.Type, p.Dice, p.Flat)).ToList();

        void Close(string type)
        {
            var at = parts.FindIndex(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                parts[at].Dice.AddRange(dice);
                parts[at] = (parts[at].Type, parts[at].Dice, parts[at].Flat + flat);
            }
            else parts.Add((type, [.. dice], flat));
            dice = [];
            flat = 0;
            pending = false;
        }
    }
}
