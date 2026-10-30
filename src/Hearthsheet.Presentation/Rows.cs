using System.Collections.ObjectModel;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Presentation;

// Row view models pair a stored domain entry (bound two-way via Model/Score) with its derived values.
// They hold no state of their own; Refresh() just tells bindings to re-read.

public abstract class RowBase : Observable
{
    internal void Refresh() => Raise(string.Empty);
}

public sealed class AbilityRow(Character c, Ability ability) : RowBase
{
    public Ability Ability => ability;
    public string Name => ability.ToString();
    public string Abbreviation => ability.Abbreviation();

    public int Score
    {
        get => c.Abilities.Get(ability);
        set => c.Abilities.Set(ability, value);
    }

    public int EffectiveScore => CharacterRules.AbilityScore(c, ability);
    public bool IsModifiedByEffects => EffectiveScore != Score;
    public int Modifier => CharacterRules.AbilityModifier(c, ability);
    public SavingThrow Save => c.Save(ability);
    public int SaveModifier => CharacterRules.SavingThrow(c, ability);
}

public sealed class SaveRow(Character c, SavingThrow save) : RowBase
{
    public SavingThrow Model => save;
    public string Name => save.Ability.ToString();
    public string Abbreviation => save.Ability.Abbreviation();
    public int Modifier => CharacterRules.SavingThrow(c, save.Ability);
}

public sealed class SkillRow(Character c, SkillEntry entry) : RowBase
{
    public SkillEntry Model => entry;
    public string Name => entry.Skill.DisplayName();
    public string AbilityAbbreviation => entry.Skill.GoverningAbility().Abbreviation();
    public int Modifier => CharacterRules.SkillModifier(c, entry.Skill);
}

/// <summary>A weapon attack shows and edits its weapon's ability, shows its derived proficiency, and a blank name follows the weapon's.</summary>
public sealed class AttackRow(Character c, Attack attack) : RowBase
{
    public Attack Model => attack;
    public Weapon? Weapon => CharacterRules.WeaponOf(c, attack);
    public bool IsWeaponAttack => Weapon is not null;

    public string Name
    {
        get => Weapon is { } w && attack.Name.Length == 0 ? w.Name : attack.Name;
        set => attack.Name = Weapon is { } w && value == w.Name ? "" : value;
    }

    public Ability? Ability
    {
        get => Weapon is { } w ? w.Ability : attack.Ability;
        set { if (Weapon is { } w) w.Ability = value; else attack.Ability = value; }
    }

    /// <summary>Read-only for a weapon attack: it comes from the weapon proficiencies.</summary>
    public bool Proficient
    {
        get => Weapon is { } w ? CharacterRules.IsProficient(c, w) : attack.Proficient;
        set { if (Weapon is null) attack.Proficient = value; }
    }

    public int AttackBonus => CharacterRules.AttackBonus(c, attack);

    /// <summary>Editable free text for a plain attack; computed (read-only) for a weapon attack.</summary>
    public string DamageRoll
    {
        get => CharacterRules.DamageRoll(c, attack);
        set { if (Weapon is null) attack.Damage = value; }
    }
}

public sealed class WeaponRow(Character c, Weapon weapon) : RowBase
{
    public Weapon Model => weapon;
    public bool Proficient => CharacterRules.IsProficient(c, weapon);
    public int AttackBonus => CharacterRules.AttackBonus(c, weapon);
    public int DamageBonus => CharacterRules.DamageBonus(c, weapon);
    public string AdditionalDamage => CharacterRules.AdditionalDamage(c, weapon);
}

public sealed class SlotPip(int level, int index, bool expended)
{
    public int Level => level;
    public int Index => index;
    public bool IsExpended => expended;
}

public sealed class SpellLevelRow : RowBase
{
    public SpellLevelRow(int level, SpellSlotLevel? slot)
    {
        Level = level;
        Slot = slot;
    }

    public int Level { get; }
    public string Title => Level == 0 ? "Cantrips" : $"Level {Level}";
    /// <summary>Null for cantrips.</summary>
    public SpellSlotLevel? Slot { get; }
    public ObservableCollection<Spell> Spells { get; } = [];

    public IReadOnlyList<SlotPip> Pips => Slot is null
        ? []
        : [.. Enumerable.Range(0, Slot.Maximum).Select(i => new SlotPip(Level, i, i >= Slot.Maximum - Slot.Expended))];
}

public sealed class ConditionToggle(Character c, Condition condition) : RowBase
{
    public Condition Condition => condition;
    public string Name => condition.ToString();

    public bool IsActive
    {
        get => c.Conditions.Contains(condition);
        set
        {
            if (value) c.AddCondition(condition);
            else c.RemoveCondition(condition);
        }
    }
}
