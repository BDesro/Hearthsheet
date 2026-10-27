using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Core.Content;

/// <summary>
/// <c>MulticlassPrerequisites</c> are alternatives (any one suffices), each a set of minimum scores (all required).
/// <c>MulticlassChoices</c> are gains the player must pick by hand (a skill, an instrument).
/// </summary>
public sealed record ClassDefinition(
    string Name, int HitDie, Ability[] SavingThrows, Ability? SpellcastingAbility, CasterType Caster,
    (Ability Ability, int Minimum)[][]? MulticlassPrerequisites = null,
    (ProficiencyCategory Category, string Name)[]? MulticlassProficiencies = null,
    string[]? MulticlassChoices = null)
{
    public (Ability Ability, int Minimum)[][] MulticlassPrerequisites { get; } = MulticlassPrerequisites ?? [];
    public (ProficiencyCategory Category, string Name)[] MulticlassProficiencies { get; } = MulticlassProficiencies ?? [];
    public string[] MulticlassChoices { get; } = MulticlassChoices ?? [];
}

/// <summary>
/// Mechanical facts from the System Reference Document 5.1 (CC-BY-4.0, see NOTICE.md): class names,
/// hit dice, save proficiencies and spell-slot progressions. No rules text is embedded. Everything here
/// is only used to pre-fill a new character; the user can edit or ignore all of it.
/// </summary>
public static class SrdCatalog
{
    private static readonly (Ability, int) Str = (Ability.Strength, 13), Dex = (Ability.Dexterity, 13),
        Int = (Ability.Intelligence, 13), Wis = (Ability.Wisdom, 13), Cha = (Ability.Charisma, 13);
    private static readonly (ProficiencyCategory, string) Light = (ProficiencyCategory.Armor, "Light armor"),
        Medium = (ProficiencyCategory.Armor, "Medium armor"), Shields = (ProficiencyCategory.Armor, "Shields"),
        Simple = (ProficiencyCategory.Weapon, "Simple weapons"), Martial = (ProficiencyCategory.Weapon, "Martial weapons"),
        Shortswords = (ProficiencyCategory.Weapon, "Shortswords"), Thieves = (ProficiencyCategory.Tool, "Thieves' tools"),
        Alchemist = (ProficiencyCategory.Tool, "Alchemist's supplies");
    private static (Ability, int)[][] Req((Ability, int) one) => [[one]];
    private static (ProficiencyCategory, string)[] Profs(params (ProficiencyCategory, string)[] p) => p;

    public static IReadOnlyList<ClassDefinition> Classes { get; } =
    [
        new("Barbarian", 12, [Ability.Strength, Ability.Constitution], null, CasterType.None,
            Req(Str), Profs(Shields, Simple, Martial)),
        new("Bard", 8, [Ability.Dexterity, Ability.Charisma], Ability.Charisma, CasterType.Full,
            Req(Cha), Profs(Light), ["one skill", "one musical instrument"]),
        new("Blood Hunter", 10, [Ability.Dexterity, Ability.Intelligence], null, CasterType.None,
            [[(Ability.Intelligence, 13), (Ability.Strength, 13)], [(Ability.Intelligence, 13), (Ability.Dexterity, 13)]],
            Profs(Light, Medium, Shields, Simple, Martial, Alchemist)),
        new("Cleric", 8, [Ability.Wisdom, Ability.Charisma], Ability.Wisdom, CasterType.Full,
            Req(Wis), Profs(Light, Medium, Shields)),
        new("Druid", 8, [Ability.Intelligence, Ability.Wisdom], Ability.Wisdom, CasterType.Full,
            Req(Wis), Profs(Light, Medium, Shields)),
        new("Fighter", 10, [Ability.Strength, Ability.Constitution], null, CasterType.None,
            [[(Ability.Strength, 13)], [(Ability.Dexterity, 13)]], Profs(Light, Medium, Shields, Simple, Martial)),
        new("Monk", 8, [Ability.Strength, Ability.Dexterity], null, CasterType.None,
            [[(Ability.Dexterity, 13), (Ability.Wisdom, 13)]], Profs(Simple, Shortswords)),
        new("Paladin", 10, [Ability.Wisdom, Ability.Charisma], Ability.Charisma, CasterType.Half,
            [[(Ability.Strength, 13), (Ability.Charisma, 13)]], Profs(Light, Medium, Shields, Simple, Martial)),
        new("Ranger", 10, [Ability.Strength, Ability.Dexterity], Ability.Wisdom, CasterType.Half,
            [[(Ability.Dexterity, 13), (Ability.Wisdom, 13)]], Profs(Light, Medium, Shields, Simple, Martial), ["one class skill"]),
        new("Rogue", 8, [Ability.Dexterity, Ability.Intelligence], null, CasterType.None,
            Req(Dex), Profs(Light, Thieves), ["one class skill"]),
        new("Sorcerer", 6, [Ability.Constitution, Ability.Charisma], Ability.Charisma, CasterType.Full, Req(Cha)),
        new("Warlock", 8, [Ability.Wisdom, Ability.Charisma], Ability.Charisma, CasterType.Pact,
            Req(Cha), Profs(Light, Simple)),
        new("Wizard", 6, [Ability.Intelligence, Ability.Wisdom], Ability.Intelligence, CasterType.Full, Req(Int)),
    ];

    public static IReadOnlyList<string> Species { get; } =
        ["Dragonborn", "Dwarf", "Elf", "Gnome", "Half-Elf", "Half-Orc", "Halfling", "Human", "Tiefling"];

    public static IReadOnlyList<string> Alignments { get; } =
    [
        "Lawful Good", "Neutral Good", "Chaotic Good", "Lawful Neutral", "True Neutral",
        "Chaotic Neutral", "Lawful Evil", "Neutral Evil", "Chaotic Evil", "Unaligned",
    ];

    public static IReadOnlyList<int> HitDieSizes { get; } = [6, 8, 10, 12];

    /// <summary>Suggested weapon types and weapon proficiencies: the categories, then the SRD weapons.</summary>
    public static IReadOnlyList<string> WeaponTypes { get; } =
    [
        "Simple", "Martial", "Melee", "Ranged",
        "Battleaxe", "Blowgun", "Club", "Dagger", "Dart", "Flail", "Glaive", "Greataxe", "Greatclub", "Greatsword",
        "Halberd", "Hand crossbow", "Handaxe", "Heavy crossbow", "Javelin", "Lance", "Light crossbow", "Light hammer",
        "Longbow", "Longsword", "Mace", "Maul", "Morningstar", "Net", "Pike", "Quarterstaff", "Rapier", "Scimitar",
        "Shortbow", "Shortsword", "Sickle", "Sling", "Spear", "Trident", "War pick", "Warhammer", "Whip",
    ];

    // Full-caster slots per character level (index 0 = level 1), spell levels 1..9.
    private static readonly int[][] FullCasterSlots =
    [
        [2], [3], [4, 2], [4, 3], [4, 3, 2], [4, 3, 3], [4, 3, 3, 1], [4, 3, 3, 2], [4, 3, 3, 3, 1],
        [4, 3, 3, 3, 2], [4, 3, 3, 3, 2, 1], [4, 3, 3, 3, 2, 1], [4, 3, 3, 3, 2, 1, 1], [4, 3, 3, 3, 2, 1, 1],
        [4, 3, 3, 3, 2, 1, 1, 1], [4, 3, 3, 3, 2, 1, 1, 1], [4, 3, 3, 3, 2, 1, 1, 1, 1],
        [4, 3, 3, 3, 3, 1, 1, 1, 1], [4, 3, 3, 3, 3, 2, 1, 1, 1], [4, 3, 3, 3, 3, 2, 2, 1, 1],
    ];

    public static ClassDefinition? FindClass(string name) =>
        Classes.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Spell slots (levels 1..9) from the SRD multiclass rules. A lone caster uses its own table (half and third
    /// casters round up, with nothing before level 2 or 3); several casters sum the rounded-down levels. Pact
    /// Magic is separate (<see cref="PactSlots"/>).
    /// </summary>
    public static int[] SpellSlots(IEnumerable<ClassLevel> classes)
    {
        var casters = classes.Where(e => e.Caster is CasterType.Full or CasterType.Half or CasterType.Third).ToList();
        var effective = casters.Count == 1
            ? casters[0].Caster switch
            {
                CasterType.Full => casters[0].Level,
                CasterType.Half => casters[0].Level < 2 ? 0 : (casters[0].Level + 1) / 2,
                _ => casters[0].Level < 3 ? 0 : (casters[0].Level + 2) / 3,
            }
            : casters.Sum(e => e.Caster switch
            {
                CasterType.Full => e.Level,
                CasterType.Half => e.Level / 2,
                _ => e.Level / 3,
            });
        var slots = new int[9];
        if (effective > 0) FullCasterSlots[Math.Min(effective, 20) - 1].CopyTo(slots, 0);
        return slots;
    }

    public static (int Count, int SlotLevel) PactSlots(int level) => level switch
    {
        1 => (1, 1),
        2 => (2, 1),
        <= 4 => (2, 2),
        <= 6 => (2, 3),
        <= 8 => (2, 4),
        <= 10 => (2, 5),
        <= 16 => (3, 5),
        _ => (4, 5),
    };
}

public sealed record NewCharacterOptions(
    string Name, string ClassName, int Level, string Species, string Background, string PlayerName = "");

public static class CharacterFactory
{
    public static Character Create(NewCharacterOptions options)
    {
        var c = new Character();
        c.Identity.Name = string.IsNullOrWhiteSpace(options.Name) ? "New Character" : options.Name.Trim();
        c.Identity.Classes[0].Name = options.ClassName.Trim();
        c.Identity.Classes[0].Level = options.Level;
        c.Identity.Species = options.Species.Trim();
        c.Identity.Background = options.Background.Trim();
        c.Identity.PlayerName = options.PlayerName.Trim();
        if (SrdCatalog.FindClass(options.ClassName) is not null) ApplyClassDefaults(c);
        c.Identity.Classes[0].HitDiceRemaining = c.Identity.Classes[0].Level;
        c.Combat.CurrentHp = CharacterRules.MaxHitPoints(c);
        return c;
    }

    /// <summary>
    /// Recomputes every automated value from the class entries (spec 7.2): overwrites hit die, caster type and
    /// spellcasting ability from the catalog for built-in classes (an Eldritch Knight or Arcane Trickster stays Third), takes saves from the first entry, sets average
    /// maximum HP, refills hit dice and recomputes slots. Custom classes keep their stored values, and proficiencies are not touched.
    /// </summary>
    public static void ApplyClassDefaults(Character c)
    {
        var classes = c.Identity.Classes;
        foreach (var e in classes)
        {
            if (SrdCatalog.FindClass(e.Name) is not { } def) continue;
            (e.HitDie, e.Caster, e.SpellcastingAbility) = (def.HitDie, def.Caster, def.SpellcastingAbility);
            if (e.Caster == CasterType.None && ClassLevel.IsThirdCasterSubclass(e.Subclass))
                (e.Caster, e.SpellcastingAbility) = (CasterType.Third, Ability.Intelligence);
        }

        if (SrdCatalog.FindClass(classes[0].Name) is { } first)
            foreach (var save in c.SavingThrows) save.Proficient = first.SavingThrows.Contains(save.Ability);

        var con = CharacterRules.AbilityModifier(c.Abilities.Constitution);
        c.Combat.MaxHp = Math.Max(1, classes[0].HitDie + con
            + classes.Sum(e => (e.Level - (e == classes[0] ? 1 : 0)) * Math.Max(1, e.HitDie / 2 + 1 + con)));
        foreach (var e in classes) e.HitDiceRemaining = e.Level;

        c.Spellcasting.SpellcastingClass = CharacterRules.PrimaryCaster(c)?.Name ?? "";
        RecomputeSlots(c);
        // Unlike LevelUp, this recomputes from the class, so regular slots with no source go to 0. Hand-set pact slots are left alone.
        if (!classes.Any(e => e.Caster is CasterType.Full or CasterType.Half or CasterType.Third))
            foreach (var slot in c.Spellcasting.Slots) (slot.Maximum, slot.Expended) = (0, 0);
    }

    /// <summary>Recomputes only what has a source (spec 6): regular slots if a Full/Half/Third entry exists, pact slots if a Pact entry does.</summary>
    internal static void RecomputeSlots(Character c)
    {
        var classes = c.Identity.Classes;
        if (classes.Any(e => e.Caster is CasterType.Full or CasterType.Half or CasterType.Third))
        {
            var slots = SrdCatalog.SpellSlots(classes);
            foreach (var slot in c.Spellcasting.Slots)
            {
                slot.Maximum = slots[slot.Level - 1];
                slot.Expended = Math.Min(slot.Expended, slot.Maximum);
            }
        }
        if (classes.Where(e => e.Caster == CasterType.Pact).Sum(e => e.Level) is > 0 and var pact)
        {
            var (count, slotLevel) = SrdCatalog.PactSlots(pact);
            var p = c.Spellcasting.PactSlots;
            p.Level = slotLevel;
            p.Maximum = count;
            p.Expended = Math.Min(p.Expended, count);
        }
    }

    /// <summary>
    /// Prerequisite warnings for adding <paramref name="className"/> as a new, non-first class: unmet ones for it and for every
    /// existing class. Empty when it is already an entry or the character has no named class yet.
    /// </summary>
    public static IReadOnlyList<string> MulticlassWarnings(Character c, string className)
    {
        className = className.Trim();
        var existing = c.Identity.Classes.Where(e => e.Name.Trim().Length > 0).ToList();
        if (existing.Count == 0 || existing.Any(e => string.Equals(e.Name.Trim(), className, StringComparison.OrdinalIgnoreCase))) return [];
        return [.. existing.Select(e => e.Name).Append(className)
            .Select(SrdCatalog.FindClass).OfType<ClassDefinition>()
            .Where(d => d.MulticlassPrerequisites.Length > 0
                && !d.MulticlassPrerequisites.Any(alt => alt.All(r => CharacterRules.AbilityScore(c, r.Ability) >= r.Minimum)))
            .Select(d => $"{d.Name} multiclassing needs " + string.Join(" or ",
                d.MulticlassPrerequisites.Select(alt => alt.Length > 1
                    ? "(" + string.Join(" and ", alt.Select(r => $"{r.Ability} {r.Minimum}")) + ")"
                    : $"{alt[0].Ability} {alt[0].Minimum}")) + ".")];
    }

    /// <summary>Adds a level in an existing or new class (spec 7.1). Saving throws are not changed.</summary>
    public static LevelUpReport LevelUp(Character c, string className, LevelUpOptions options)
    {
        var report = new LevelUpReport();
        className = className.Trim();
        if (CharacterRules.TotalLevel(c) >= 20)
        {
            report.Completed = false;
            report.NeedsAttention.Add("Already at level 20; nothing to level up.");
            return report;
        }

        var classes = c.Identity.Classes;
        var existing = classes.FirstOrDefault(e => string.Equals(e.Name.Trim(), className, StringComparison.OrdinalIgnoreCase));
        var def = SrdCatalog.FindClass(className);
        // The entry's setters clamp, so HP and roll validation use the stored die.
        var entry = existing ?? new ClassLevel
        {
            Name = def?.Name ?? className,
            HitDie = def?.HitDie ?? options.HitDie ?? 8,
            Caster = def?.Caster ?? options.Caster ?? CasterType.None,
            SpellcastingAbility = def?.SpellcastingAbility ?? options.Ability,
        };
        var die = entry.HitDie;
        if (options.Roll is { } r && (r < 1 || r > die))
            throw new ArgumentOutOfRangeException(nameof(options), $"Roll must be between 1 and {die}.");

        if (existing is not null)
        {
            entry.Level++;
            entry.HitDiceRemaining++;
        }
        else
        {
            report.Warnings.AddRange(MulticlassWarnings(c, className));
            entry.HitDiceRemaining = 1;
            classes.Add(entry);
            if (def is null) report.NeedsAttention.Add($"Add {entry.Name} proficiencies by hand.");
            else
            {
                foreach (var (category, name) in def.MulticlassProficiencies)
                {
                    if (CharacterRules.HasProficiency(c, category, name)) continue;
                    c.Proficiencies.Add(new Proficiency { Category = category, Name = name });
                    report.Changes.Add($"Gained {name} proficiency.");
                }
                foreach (var choice in def.MulticlassChoices)
                    report.NeedsAttention.Add($"Choose {choice} ({def.Name} multiclass).");
            }
        }

        var gain = options.Roll is { } roll ? Math.Max(1, roll + RawConModifier(c)) : AverageHitPointGain(c, die);
        c.Combat.MaxHp += gain;
        c.Combat.CurrentHp += gain;
        report.Changes.Add($"{entry.Name} is now level {entry.Level} (total {CharacterRules.TotalLevel(c)}); +{gain} maximum HP.");

        RecomputeSlots(c);
        report.NeedsAttention.Add($"Add {entry.Name} {entry.Level} features by hand.");
        return report;
    }

    /// <summary>HP gained for a level at the average roll. Uses the stored CON score: max HP is stored, so an item's CON must not be baked in.</summary>
    public static int AverageHitPointGain(Character c, int hitDie) => Math.Max(1, hitDie / 2 + 1 + RawConModifier(c));

    private static int RawConModifier(Character c) => CharacterRules.AbilityModifier(c.Abilities.Constitution);
}

public sealed record LevelUpOptions(int? Roll = null, int? HitDie = null, CasterType? Caster = null, Ability? Ability = null);

public sealed class LevelUpReport
{
    public bool Completed { get; set; } = true;
    public List<string> Changes { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> NeedsAttention { get; } = [];
}
