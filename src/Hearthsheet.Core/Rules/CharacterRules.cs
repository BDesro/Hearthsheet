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
public static class CharacterRules
{
    public static int AbilityModifier(int score) => (int)Math.Floor((score - 10) / 2.0);

    public static int ProficiencyBonus(int level) => 2 + (Math.Clamp(level, 1, 20) - 1) / 4;

    public static int ProficiencyBonus(Character c) => ProficiencyBonus(c.Identity.Level);

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

    public static int HitDiceTotal(Character c) => c.Identity.Level;

    public static int? SpellSaveDc(Character c) => c.Spellcasting.Ability is { } a
        ? Resolve(c, 8 + ProficiencyBonus(c) + AbilityModifier(c, a), StatTargets.SpellDc)
        : null;

    public static int? SpellAttackBonus(Character c) => c.Spellcasting.Ability is { } a
        ? Resolve(c, ProficiencyBonus(c) + AbilityModifier(c, a), StatTargets.SpellAttack)
        : null;

    public static Weapon? WeaponOf(Character c, Attack attack) =>
        attack.WeaponId is { } id ? c.Weapons.FirstOrDefault(w => w.Id == id) : null;

    /// <summary>A weapon attack takes its ability, proficiency and bonuses from the weapon, plus the attack's own bonus.</summary>
    public static int AttackBonus(Character c, Attack attack) => WeaponOf(c, attack) is { } w
        ? AttackBonus(c, w) + attack.Bonus
        : ToHit(c, attack.Ability, attack.Proficient, attack.Bonus, []);

    public static int AttackBonus(Character c, Weapon w) =>
        ToHit(c, w.Ability, w.Proficient, w.ToHitBonus + w.Effects.Sum(e => e.ToHit), WeaponTypes(w));

    private static int ToHit(Character c, Ability? ability, bool proficient, int bonus, string[] types)
    {
        var baseValue = (ability is { } a ? AbilityModifier(c, a) : 0) + (proficient ? ProficiencyBonus(c) : 0) + bonus;
        return Resolve(c, baseValue, [StatTargets.Attack, .. Qualifiers(ability, types).Select(StatTargets.AttackWith)]);
    }

    /// <summary>The flat number in the weapon's main damage: ability modifier, bonus damage, untyped flat effects and damage modifiers.</summary>
    public static int DamageBonus(Character c, Weapon w)
    {
        var baseValue = (w.Ability is { } a ? AbilityModifier(c, a) : 0) + w.BonusDamage
                        + w.Effects.Where(e => IsMainDamage(w, e)).Sum(e => int.TryParse(e.Damage, out var n) ? n : 0);
        return Resolve(c, baseValue, [StatTargets.Damage, .. Qualifiers(w.Ability, WeaponTypes(w)).Select(StatTargets.DamageWith)]);
    }

    /// <summary>
    /// The attack's full damage, grouped by type with the weapon's main type first,
    /// e.g. "1d8 + 3 slashing + 2d6 fire". A non-weapon attack's free text is returned as is.
    /// </summary>
    public static string DamageRoll(Character c, Attack attack)
    {
        if (WeaponOf(c, attack) is not { } w) return attack.Damage;
        var groups = new List<(string Type, List<string> Dice, int Flat)>
        {
            (w.DamageType.Trim(), [string.IsNullOrWhiteSpace(attack.Dice) ? w.Damage.Trim() : attack.Dice.Trim()], DamageBonus(c, w)),
        };
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
        var parts = groups.Select(g => $"{FormatDice(g.Dice.Where(d => d.Length > 0).ToList(), g.Flat)} {g.Type}".Trim())
            .Append(w.AdditionalDamage.Trim());
        return string.Join(" + ", parts.Where(p => p.Length > 0));
    }

    private static string FormatDice(List<string> dice, int flat) =>
        dice.Count == 0 ? (flat == 0 ? "" : flat.ToString())
        : string.Join(" + ", dice) + (flat > 0 ? $" + {flat}" : flat < 0 ? $" − {-flat}" : "");

    private static bool IsMainDamage(Weapon w, WeaponEffect e) =>
        string.IsNullOrWhiteSpace(e.DamageType) || string.Equals(e.DamageType.Trim(), w.DamageType.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string[] WeaponTypes(Weapon w) =>
        w.Types.Split([',', ' ', '/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> Qualifiers(Ability? ability, string[] types) =>
        ability is { } a ? types.Prepend(a.Abbreviation()) : types;

    public static double CarriedWeight(Character c) => c.Items.Concat(c.WondrousItems).Sum(i => i.Weight * i.Quantity);

    /// <summary>Standard carrying capacity (STR × 15 lb).</summary>
    public static int CarryingCapacity(Character c) => AbilityScore(c, Ability.Strength) * 15;

    public static bool IsDead(Character c) => c.Combat.DeathSaveFailures >= 3;

    public static bool IsDying(Character c) => c.Combat.CurrentHp == 0 && !IsDead(c) && !c.Combat.IsStable;

    public static string FormatModifier(int value) => value >= 0 ? $"+{value}" : $"−{-value}";
}
