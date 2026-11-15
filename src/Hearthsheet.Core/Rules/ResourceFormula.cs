using System.Globalization;
using System.Text.RegularExpressions;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Core.Rules;

public sealed record ResolvedRoll(IReadOnlyList<DiceTerm> Dice, int Flat, string Description);

/// <summary>Reads resource roll formulas such as "1d10 + fighter level" and resolves them against a character.</summary>
public static class ResourceFormula
{
    public const string ReadError =
        "Can't read this roll. Use dice, numbers, level, a class level, proficiency or an ability, joined by + or -.";

    private const int MaxTerms = 20;

    private enum Kind { Dice, Number, Level, ClassLevel, Proficiency, Ability }

    private readonly record struct Token(bool Negative, Kind Kind, int A, int B, string Text, Ability Ability);

    private static readonly Regex Dice = new(@"^([0-9]{0,3})d([0-9]{1,3})$", RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^[0-9]{1,3}$", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, Ability> Abilities = new()
    {
        ["str"] = Ability.Strength, ["strength"] = Ability.Strength,
        ["dex"] = Ability.Dexterity, ["dexterity"] = Ability.Dexterity,
        ["con"] = Ability.Constitution, ["constitution"] = Ability.Constitution,
        ["int"] = Ability.Intelligence, ["intelligence"] = Ability.Intelligence,
        ["wis"] = Ability.Wisdom, ["wisdom"] = Ability.Wisdom,
        ["cha"] = Ability.Charisma, ["charisma"] = Ability.Charisma,
    };

    /// <summary>Syntax only: true exactly when <see cref="TryResolve"/> would succeed for some character.</summary>
    public static bool IsReadable(string? formula) => Parse(formula) is not null;

    public static ResolvedRoll? TryResolve(Character c, string? formula)
    {
        if (Parse(formula) is not { } tokens) return null;

        var dice = new List<DiceTerm>();
        var flat = 0;
        var text = new System.Text.StringBuilder();

        foreach (var t in tokens)
        {
            if (t.Kind == Kind.Dice)
            {
                dice.Add(new DiceTerm(t.A, t.B));
                Append(text, false, $"{t.A}d{t.B}");
                continue;
            }

            int value;
            string? label = null;
            switch (t.Kind)
            {
                case Kind.Number: value = t.A; break;
                case Kind.Level: value = CharacterRules.TotalLevel(c); label = "level"; break;
                case Kind.Proficiency: value = CharacterRules.ProficiencyBonus(c); label = "proficiency"; break;
                case Kind.Ability: value = CharacterRules.AbilityModifier(c, t.Ability); label = t.Text; break;
                default:
                    value = c.Identity.Classes
                        .Where(e => string.Equals(Spaces.Replace(e.Name, " ").Trim(), t.Text, StringComparison.OrdinalIgnoreCase))
                        .Sum(e => e.Level);
                    label = value == 0 ? $"no {t.Text} levels" : $"{t.Text} level";
                    break;
            }

            var signed = t.Negative ? -value : value;
            flat += signed;
            Append(text, signed < 0, label is null ? $"{Math.Abs(signed)}" : $"{Math.Abs(signed)} ({label})");
        }

        return new ResolvedRoll(dice, flat, text.ToString());
    }

    private static void Append(System.Text.StringBuilder sb, bool minus, string term)
    {
        if (sb.Length == 0) sb.Append(minus ? "−" : "");
        else sb.Append(minus ? " − " : " + ");
        sb.Append(term);
    }

    private static List<Token>? Parse(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;

        // Split keeps the separators: term, sign, term, sign, ... A hyphen between two letters is part of a
        // word ("spell-sword level"), not a minus.
        var parts = Regex.Split(formula, @"(\+|−|(?<!\p{L})-|-(?!\p{L}))");
        var leading = parts[0].Trim().Length == 0 && parts.Length > 1;
        var tokens = new List<Token>();
        for (var i = leading ? 2 : 0; i < parts.Length; i += 2)
        {
            var negative = i > 0 && parts[i - 1] != "+";
            if (ParseTerm(parts[i], negative) is not { } token) return null;
            tokens.Add(token);
            if (tokens.Count > MaxTerms) return null;
        }
        return tokens;
    }

    private static Token? ParseTerm(string raw, bool negative)
    {
        var term = Spaces.Replace(raw, " ").Trim().ToLowerInvariant();
        if (term.Length == 0) return null;

        if (Dice.Match(term) is { Success: true } d)
        {
            var count = d.Groups[1].Length == 0 ? 1 : int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
            var sides = int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture);
            if (negative || count is < 1 or > 100 || !DamageText.ValidSides.Contains(sides)) return null;
            return new Token(false, Kind.Dice, count, sides, "", default);
        }
        if (Number.IsMatch(term)) return new Token(negative, Kind.Number, int.Parse(term, CultureInfo.InvariantCulture), 0, "", default);
        if (term == "level") return new Token(negative, Kind.Level, 0, 0, "", default);
        if (term == "proficiency") return new Token(negative, Kind.Proficiency, 0, 0, "", default);
        if (Abilities.TryGetValue(term, out var a))
            return new Token(negative, Kind.Ability, 0, 0, a.ToString()[..3].ToUpperInvariant(), a);
        if (term.EndsWith(" level") && term[..^6].Trim() is { Length: > 0 } cls)
            return new Token(negative, Kind.ClassLevel, 0, 0, cls, default);
        return null;
    }
}
