using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Core.Rules;

/// <summary>Keys that modifiers can target. Free-form strings keep new targets cheap to add.</summary>
public static class StatTargets
{
    public const string ArmorClass = "ac";
    public const string Initiative = "initiative";
    public const string Speed = "speed";
    public const string MaxHp = "hp.max";
    public const string AllSaves = "save.all";
    public const string AllSkills = "skill.all";
    public const string Attack = "attack";
    /// <summary>Flat damage added to weapon attacks' main damage.</summary>
    public const string Damage = "damage";
    public const string SpellDc = "spell.dc";
    public const string SpellAttack = "spell.attack";
    public const string PassivePerception = "passive.perception";

    public static string AbilityScore(Ability a) => "ability." + a.Abbreviation().ToLowerInvariant();
    public static string Save(Ability a) => "save." + a.Abbreviation().ToLowerInvariant();
    public static string SkillCheck(Skill s) => "skill." + s.ToString().ToLowerInvariant();

    /// <summary>The attack's ability ("attack.str") or a weapon type ("attack.melee").</summary>
    public static string AttackWith(string abilityOrType) => Attack + "." + abilityOrType.ToLowerInvariant();
    public static string DamageWith(string abilityOrType) => Damage + "." + abilityOrType.ToLowerInvariant();

    private static readonly string[] CommonWeaponTypes = ["simple", "martial", "melee", "ranged"];

    public static IReadOnlyList<string> All { get; } =
    [
        ArmorClass, Initiative, Speed, MaxHp, Attack, Damage, SpellDc, SpellAttack, PassivePerception, AllSaves, AllSkills,
        .. AbilityInfo.All.Select(AbilityScore),
        .. AbilityInfo.All.Select(Save),
        .. SkillInfo.All.Select(SkillCheck),
        .. AbilityInfo.All.Select(a => AttackWith(a.Abbreviation())),
        .. CommonWeaponTypes.Select(AttackWith),
        .. AbilityInfo.All.Select(a => DamageWith(a.Abbreviation())),
        .. CommonWeaponTypes.Select(DamageWith),
    ];
}

/// <summary>
/// Derived values. Pure functions over stored state so nothing derived is ever persisted or
/// allowed to drift out of sync between views.
/// </summary>
public sealed record HitDicePool(int Size, int Remaining, int Total);

public static class CharacterRules
{
    public static int AbilityModifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    public static int ProficiencyBonus(int level) => 2 + (Math.Clamp(level, 1, 20) - 1) / 4;

    public static int TotalLevel(Character c) => c.Identity.Classes.Sum(e => e.Level);

    public static int ProficiencyBonus(Character c) => ProficiencyBonus(TotalLevel(c));

    /// <summary>Modifiers from features, active effects, and equipped (and, where required, attuned) items.</summary>
    public static IEnumerable<Modifier> ActiveModifiers(Character c) =>
        c.Features.SelectMany(f => f.Modifiers)
            .Concat(c.Effects.SelectMany(e => e.Modifiers))
            .Concat(c.Items.Concat(c.WondrousItems).Where(IsItemActive).SelectMany(i => i.Modifiers));

    public static bool IsItemActive(Item i) => i.Equipped && (!i.RequiresAttunement || i.Attuned);

    /// <summary>Highest Set modifier replaces the base (if any), then all Bonus modifiers add.</summary>
    public static int Resolve(Character c, int baseValue, params string[] targets)
    {
        var relevant = ActiveModifiers(c).Where(m => targets.Contains(m.Target)).ToList();
        var sets = relevant.Where(m => m.Kind == ModifierKind.Set).ToList();
        var value = sets.Count > 0 ? sets.Max(m => m.Value) : baseValue;
        return value + relevant.Where(m => m.Kind == ModifierKind.Bonus).Sum(m => m.Value);
    }

    public static int AbilityScore(Character c, Ability a) =>
        Math.Clamp(Resolve(c, c.Abilities.Get(a), StatTargets.AbilityScore(a)), 1, 30);

    public static int AbilityModifier(Character c, Ability a) => AbilityModifier(AbilityScore(c, a));

    public static int ProficiencyContribution(ProficiencyLevel level, int proficiencyBonus) => level switch
    {
        ProficiencyLevel.Half => proficiencyBonus / 2,
        ProficiencyLevel.Proficient => proficiencyBonus,
        ProficiencyLevel.Expertise => proficiencyBonus * 2,
        _ => 0,
    };

    public static int SavingThrow(Character c, Ability a)
    {
        var entry = c.Save(a);
        var baseValue = AbilityModifier(c, a) + (entry.Proficient ? ProficiencyBonus(c) : 0) + entry.Bonus;
        return Resolve(c, baseValue, StatTargets.AllSaves, StatTargets.Save(a));
    }

    public static int SkillModifier(Character c, Skill s)
    {
        var entry = c.SkillEntry(s);
        var baseValue = AbilityModifier(c, s.GoverningAbility())
                        + ProficiencyContribution(entry.Proficiency, ProficiencyBonus(c))
                        + entry.Bonus;
        return Resolve(c, baseValue, StatTargets.AllSkills, StatTargets.SkillCheck(s));
    }

    public static int PassivePerception(Character c) =>
        Resolve(c, 10 + SkillModifier(c, Skill.Perception), StatTargets.PassivePerception);

    public static int ArmorClass(Character c) => Resolve(c, c.Combat.ArmorClass, StatTargets.ArmorClass);

    public static int Initiative(Character c) =>
        Resolve(c, AbilityModifier(c, Ability.Dexterity) + c.Combat.InitiativeBonus, StatTargets.Initiative);

    public static int Speed(Character c) => Math.Max(0, Resolve(c, c.Combat.Speed, StatTargets.Speed));

    public static int MaxHitPoints(Character c) => Math.Max(1, Resolve(c, c.Combat.MaxHp, StatTargets.MaxHp));

    public static int HitDiceTotal(Character c) => TotalLevel(c);

    /// <summary>Hit dice grouped by die size, largest first.</summary>
    public static IReadOnlyList<HitDicePool> HitDicePools(Character c) =>
        [.. c.Identity.Classes.GroupBy(e => e.HitDie)
            .Select(g => new HitDicePool(g.Key, g.Sum(e => e.HitDiceRemaining), g.Sum(e => e.Level)))
            .OrderByDescending(p => p.Size)];

    /// <summary>"Fighter" for one class; "Fighter 11 (Echo Knight) / Blood Hunter 1" for several.</summary>
    public static string ClassSummary(Character c)
    {
        var classes = c.Identity.Classes;
        if (classes.Count == 1) return classes[0].Name;
        return string.Join(" / ", classes.Select(e =>
            $"{e.Name} {e.Level}{(string.IsNullOrWhiteSpace(e.Subclass) ? "" : $" ({e.Subclass})")}".Trim()));
    }

    /// <summary>"Fighter 11" for one class (total level appended); the multiclass summary already carries each level.</summary>
    public static string ClassAndLevelText(Character c) =>
        c.Identity.Classes.Count == 1 ? $"{ClassSummary(c)} {TotalLevel(c)}".Trim() : ClassSummary(c);

    // First entry with an ability; Task 2 replaces this with a per-class lookup.
    private static Ability? SpellAbility(Character c) => c.Identity.Classes.FirstOrDefault(e => e.SpellcastingAbility is not null)?.SpellcastingAbility;

    public static int? SpellSaveDc(Character c) => SpellAbility(c) is { } a
        ? Resolve(c, 8 + ProficiencyBonus(c) + AbilityModifier(c, a), StatTargets.SpellDc)
        : null;

    public static int? SpellAttackBonus(Character c) => SpellAbility(c) is { } a
        ? Resolve(c, ProficiencyBonus(c) + AbilityModifier(c, a), StatTargets.SpellAttack)
        : null;

    public static Weapon? WeaponOf(Character c, Attack attack) =>
        attack.WeaponId is { } id ? c.Weapons.FirstOrDefault(w => w.Id == id) : null;

    /// <summary>A weapon attack takes its ability, proficiency and bonuses from the weapon, plus the attack's own bonus.</summary>
    public static int AttackBonus(Character c, Attack attack) => WeaponOf(c, attack) is { } w
        ? AttackBonus(c, w) + attack.Bonus
        : ToHit(c, attack.Ability, attack.Proficient, attack.Bonus, []);

    public static int AttackBonus(Character c, Weapon w) =>
        ToHit(c, w.Ability, IsProficient(c, w), w.Effects.Sum(e => e.ToHit), WeaponTypes(w));

    /// <summary>
    /// True when a weapon proficiency names the weapon or one of its types. An entry may list several
    /// ("Simple, Martial"); a trailing "weapon(s)" and plural s are ignored ("Martial weapons", "Longswords").
    /// </summary>
    public static bool IsProficient(Character c, Weapon w)
    {
        var keys = WeaponTypes(w).Append(w.Name).Select(ProficiencyKey).Where(k => k.Length > 0).ToHashSet();
        return c.Proficiencies.Where(p => p.Category == ProficiencyCategory.Weapon)
            .SelectMany(p => SplitList(p.Name))
            .Any(name => keys.Contains(ProficiencyKey(name)));
    }

    /// <summary>
    /// Choices for weapon types and weapon proficiencies: the categories, then the custom types already used on this
    /// character's weapons or weapon proficiencies, then the SRD weapons. Custom types live only where they're used.
    /// </summary>
    public static IReadOnlyList<string> WeaponTypeOptions(Character c)
    {
        var standard = SrdCatalog.WeaponTypes;
        var standardKeys = standard.Select(ProficiencyKey).ToHashSet();
        var custom = c.Weapons.SelectMany(w => SplitList(w.Types))
            .Concat(c.Proficiencies.Where(p => p.Category == ProficiencyCategory.Weapon).SelectMany(p => SplitList(p.Name)))
            .Where(t => !standardKeys.Contains(ProficiencyKey(t)))
            .DistinctBy(ProficiencyKey)
            .Order(StringComparer.OrdinalIgnoreCase);
        return [.. standard.Take(4), .. custom, .. standard.Skip(4)];
    }

    private static string ProficiencyKey(string name)
    {
        var key = name.Trim().ToLowerInvariant();
        foreach (var suffix in (string[])[" weapons", " weapon"])
            if (key.EndsWith(suffix, StringComparison.Ordinal)) key = key[..^suffix.Length];
        return key.TrimEnd('s');
    }

    private static int ToHit(Character c, Ability? ability, bool proficient, int bonus, string[] types)
    {
        var baseValue = (ability is { } a ? AbilityModifier(c, a) : 0) + (proficient ? ProficiencyBonus(c) : 0) + bonus;
        return Resolve(c, baseValue, [StatTargets.Attack, .. Qualifiers(ability, types).Select(StatTargets.AttackWith)]);
    }

    /// <summary>The flat number in the weapon's main damage: ability modifier, untyped flat effects and damage modifiers.</summary>
    public static int DamageBonus(Character c, Weapon w)
    {
        var baseValue = (w.Ability is { } a ? AbilityModifier(c, a) : 0)
                        + w.Effects.Where(e => IsMainDamage(w, e)).Sum(e => int.TryParse(e.Damage, out var n) ? n : 0);
        return Resolve(c, baseValue, [StatTargets.Damage, .. Qualifiers(w.Ability, WeaponTypes(w)).Select(StatTargets.DamageWith)]);
    }

    /// <summary>
    /// The attack's full damage, grouped by type with the weapon's main type first,
    /// e.g. "1d8 + 3 slashing + 2d6 fire". A non-weapon attack's free text is returned as is.
    /// </summary>
    public static string DamageRoll(Character c, Attack attack) => WeaponOf(c, attack) is { } w
        ? string.Join(" + ", DamageParts(c, w, string.IsNullOrWhiteSpace(attack.Dice) ? w.Damage : attack.Dice).Where(p => p.Length > 0))
        : attack.Damage;

    /// <summary>The weapon's extra typed damage from its effects, e.g. "2d6 fire".</summary>
    public static string AdditionalDamage(Character c, Weapon w) => string.Join(" + ", DamageParts(c, w, w.Damage).Skip(1));

    /// <summary>One part per damage type; the first is always the weapon's main damage (possibly empty).</summary>
    private static List<string> DamageParts(Character c, Weapon w, string dice)
    {
        var groups = new List<(string Type, List<string> Dice, int Flat)> { (w.DamageType.Trim(), [dice.Trim()], DamageBonus(c, w)) };
        foreach (var e in w.Effects.Where(e => !string.IsNullOrWhiteSpace(e.Damage)))
        {
            var main = IsMainDamage(w, e);
            if (main && int.TryParse(e.Damage, out _)) continue; // already in DamageBonus
            var type = main ? groups[0].Type : e.DamageType.Trim();
            var i = groups.FindIndex(g => string.Equals(g.Type, type, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                groups.Add((type, [], 0));
                i = groups.Count - 1;
            }
            if (int.TryParse(e.Damage, out var flat)) groups[i] = (groups[i].Type, groups[i].Dice, groups[i].Flat + flat);
            else groups[i].Dice.Add(e.Damage.Trim());
        }
        return [.. groups.Select(g => $"{FormatDice(g.Dice.Where(d => d.Length > 0).ToList(), g.Flat)} {g.Type}".Trim())];
    }

    private static string FormatDice(List<string> dice, int flat) =>
        dice.Count == 0 ? (flat == 0 ? "" : flat.ToString())
        : string.Join(" + ", dice) + (flat > 0 ? $" + {flat}" : flat < 0 ? $" − {-flat}" : "");

    private static bool IsMainDamage(Weapon w, WeaponEffect e) =>
        string.IsNullOrWhiteSpace(e.DamageType) || string.Equals(e.DamageType.Trim(), w.DamageType.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a comma-separated list such as a weapon's types ("Martial, Melee, Light crossbow").</summary>
    public static string[] SplitList(string list) =>
        list.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] WeaponTypes(Weapon w) => SplitList(w.Types);

    private static IEnumerable<string> Qualifiers(Ability? ability, string[] types) =>
        ability is { } a ? types.Prepend(a.Abbreviation()) : types;

    public static double CarriedWeight(Character c) => c.Items.Concat(c.WondrousItems).Sum(i => i.Weight * i.Quantity);

    /// <summary>Standard carrying capacity (STR × 15 lb).</summary>
    public static int CarryingCapacity(Character c) => AbilityScore(c, Ability.Strength) * 15;

    public static bool IsDead(Character c) => c.Combat.DeathSaveFailures >= 3;

    public static bool IsDying(Character c) => c.Combat.CurrentHp == 0 && !IsDead(c) && !c.Combat.IsStable;

    public static string FormatModifier(int value) => value >= 0 ? $"+{value}" : $"−{-value}";
}
