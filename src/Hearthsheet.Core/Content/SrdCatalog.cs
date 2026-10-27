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
        if (SrdCatalog.FindClass(options.ClassName) is { } def) ApplyClassDefaults(c, def);
        c.Identity.Classes[0].HitDiceRemaining = c.Identity.Classes[0].Level;
        c.Combat.CurrentHp = CharacterRules.MaxHitPoints(c);
        return c;
    }

    /// <summary>Hit die, save proficiencies, average HP, spellcasting ability and slots for the class.</summary>
    public static void ApplyClassDefaults(Character c, ClassDefinition def)
    {
        var entry = c.Identity.Classes[0];
        var level = entry.Level;
        entry.HitDie = def.HitDie;
        entry.Caster = def.Caster;
        foreach (var save in c.SavingThrows) save.Proficient = def.SavingThrows.Contains(save.Ability);

        var con = CharacterRules.AbilityModifier(c.Abilities.Constitution);
        c.Combat.MaxHp = Math.Max(1, def.HitDie + con + (level - 1) * Math.Max(1, def.HitDie / 2 + 1 + con));

        entry.SpellcastingAbility = def.SpellcastingAbility;
        if (def.SpellcastingAbility is not null) c.Spellcasting.SpellcastingClass = def.Name;
        var slots = SrdCatalog.SpellSlots([entry]);
        foreach (var slot in c.Spellcasting.Slots) slot.Maximum = slots[slot.Level - 1];

        if (def.Caster == CasterType.Pact)
        {
            var (count, slotLevel) = SrdCatalog.PactSlots(level);
            c.Spellcasting.PactSlots.Level = slotLevel;
            c.Spellcasting.PactSlots.Maximum = count;
            c.Spellcasting.PactSlots.Expended = 0;
        }
    }
}
