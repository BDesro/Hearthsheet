using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Presentation;

/// <summary>Option lists for pickers, shared by every UI.</summary>
public static class Lookups
{
    public sealed record Option(object? Value, string Label);

    public static Ability[] Abilities { get; } = Enum.GetValues<Ability>();

    /// <summary>Abilities plus "none" for nullable ability fields.</summary>
    public static Option[] OptionalAbilities { get; } =
        [new(null, "None"), .. Enum.GetValues<Ability>().Select(a => new Option(a, a.ToString()))];

    public static CasterType[] CasterTypes { get; } = Enum.GetValues<CasterType>();
    public static ProficiencyLevel[] ProficiencyLevels { get; } = Enum.GetValues<ProficiencyLevel>();
    public static ProficiencyCategory[] ProficiencyCategories { get; } = Enum.GetValues<ProficiencyCategory>();
    public static FeatureSource[] FeatureSources { get; } = Enum.GetValues<FeatureSource>();
    public static RecoveryTiming[] RecoveryTimings { get; } = Enum.GetValues<RecoveryTiming>();
    public static EffectExpiry[] EffectExpiries { get; } = Enum.GetValues<EffectExpiry>();
    public static ModifierKind[] ModifierKinds { get; } = Enum.GetValues<ModifierKind>();
    public static IReadOnlyList<string> StatTargetKeys => StatTargets.All;
    public static IReadOnlyList<string> ClassNames { get; } = Hearthsheet.Core.Content.SrdCatalog.Classes.Select(c => c.Name).ToList();
    public static IReadOnlyList<string> Species => Hearthsheet.Core.Content.SrdCatalog.Species;
    public static IReadOnlyList<string> Alignments => Hearthsheet.Core.Content.SrdCatalog.Alignments;
    public static IReadOnlyList<int> HitDieSizes => Hearthsheet.Core.Content.SrdCatalog.HitDieSizes;
    public static int[] SpellLevels { get; } = [.. Enumerable.Range(0, 10)];
    public static string[] SpellSchools { get; } =
        ["Abjuration", "Conjuration", "Divination", "Enchantment", "Evocation", "Illusion", "Necromancy", "Transmutation"];
}
