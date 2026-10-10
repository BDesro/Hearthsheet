using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Tests;

public class RulesTests
{
    [Theory]
    [InlineData(1, -5)]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(15, 2)]
    [InlineData(20, 5)]
    [InlineData(30, 10)]
    public void AbilityModifier_FollowsFloorOfHalfDifference(int score, int expected) =>
        Assert.Equal(expected, CharacterRules.AbilityModifier(score));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public void ProficiencyBonus_ByLevel(int level, int expected) =>
        Assert.Equal(expected, CharacterRules.ProficiencyBonus(level));

    [Fact]
    public void Skill_CombinesAbilityProficiencyExpertiseAndBonus()
    {
        var c = new Character();
        c.Identity.Level = 5; // PB +3
        c.Abilities.Dexterity = 16; // +3

        Assert.Equal(3, CharacterRules.SkillModifier(c, Skill.Stealth));

        c.SkillEntry(Skill.Stealth).Proficiency = ProficiencyLevel.Proficient;
        Assert.Equal(6, CharacterRules.SkillModifier(c, Skill.Stealth));

        c.SkillEntry(Skill.Stealth).Proficiency = ProficiencyLevel.Expertise;
        Assert.Equal(9, CharacterRules.SkillModifier(c, Skill.Stealth));

        c.SkillEntry(Skill.Stealth).Bonus = 1;
        Assert.Equal(10, CharacterRules.SkillModifier(c, Skill.Stealth));
    }

    [Fact]
    public void Skill_HalfProficiencyRoundsDown()
    {
        var c = new Character();
        c.Identity.Level = 5; // PB 3 → half = 1
        c.SkillEntry(Skill.History).Proficiency = ProficiencyLevel.Half;
        Assert.Equal(1, CharacterRules.SkillModifier(c, Skill.History));
    }

    [Fact]
    public void SavingThrow_UsesProficiencyAndBonuses()
    {
        var c = new Character();
        c.Abilities.Wisdom = 14;
        c.Save(Ability.Wisdom).Proficient = true;
        Assert.Equal(4, CharacterRules.SavingThrow(c, Ability.Wisdom));
        Assert.Equal(0, CharacterRules.SavingThrow(c, Ability.Strength));

        // Cloak of Protection style: +1 to all saves while equipped.
        var cloak = new Item { Name = "Cloak", Equipped = true, RequiresAttunement = true, Attuned = true };
        cloak.Modifiers.Add(new Modifier { Target = StatTargets.AllSaves, Value = 1 });
        c.Items.Add(cloak);
        Assert.Equal(5, CharacterRules.SavingThrow(c, Ability.Wisdom));
        Assert.Equal(1, CharacterRules.SavingThrow(c, Ability.Strength));
    }

    [Fact]
    public void ItemModifiers_ApplyOnlyWhenEquippedAndAttunedIfRequired()
    {
        var c = new Character();
        c.Combat.ArmorClass = 15;
        var ring = new Item { Name = "Ring of Protection", RequiresAttunement = true };
        ring.Modifiers.Add(new Modifier { Target = "AC", Value = 1 }); // targets are normalized to lower case
        c.Items.Add(ring);

        Assert.Equal(15, CharacterRules.ArmorClass(c));
        ring.Equipped = true;
        Assert.Equal(15, CharacterRules.ArmorClass(c));
        ring.Attuned = true;
        Assert.Equal(16, CharacterRules.ArmorClass(c));
    }

    [Fact]
    public void Effects_SetOverridesBaseThenBonusesAdd()
    {
        var c = new Character();
        c.Abilities.Strength = 8;
        var gauntlets = new Effect { Name = "Giant Strength" };
        gauntlets.Modifiers.Add(new Modifier { Target = StatTargets.AbilityScore(Ability.Strength), Kind = ModifierKind.Set, Value = 19 });
        c.Effects.Add(gauntlets);
        Assert.Equal(19, CharacterRules.AbilityScore(c, Ability.Strength));
        Assert.Equal(4, CharacterRules.AbilityModifier(c, Ability.Strength));

        var bless = new Effect { Name = "Enhance" };
        bless.Modifiers.Add(new Modifier { Target = StatTargets.AbilityScore(Ability.Strength), Value = 2 });
        c.Effects.Add(bless);
        Assert.Equal(21, CharacterRules.AbilityScore(c, Ability.Strength));
    }

    [Fact]
    public void Derived_CombatAndSpellValues()
    {
        var c = new Character();
        c.Identity.Level = 9; // PB 4
        c.Abilities.Dexterity = 14;
        c.Abilities.Wisdom = 12;
        c.Abilities.Intelligence = 18;
        c.Combat.InitiativeBonus = 1;
        c.SkillEntry(Skill.Perception).Proficiency = ProficiencyLevel.Proficient;
        c.Spellcasting.Ability = Ability.Intelligence;

        Assert.Equal(3, CharacterRules.Initiative(c));
        Assert.Equal(15, CharacterRules.PassivePerception(c));
        Assert.Equal(16, CharacterRules.SpellSaveDc(c));
        Assert.Equal(8, CharacterRules.SpellAttackBonus(c));

        c.Spellcasting.Ability = null;
        Assert.Null(CharacterRules.SpellSaveDc(c));
    }

    [Fact]
    public void AttackBonus_UsesAbilityProficiencyAndBonus()
    {
        var c = new Character();
        c.Abilities.Strength = 16;
        var sword = new Attack { Name = "Longsword", Ability = Ability.Strength, Proficient = true, Bonus = 1 };
        Assert.Equal(6, CharacterRules.AttackBonus(c, sword));
        sword.Ability = null;
        Assert.Equal(3, CharacterRules.AttackBonus(c, sword));
    }

    [Fact]
    public void Setters_ClampOutOfRangeValues()
    {
        var c = new Character();
        c.Abilities.Strength = 99;
        c.Identity.Level = 0;
        c.Combat.CurrentHp = -5;
        Assert.Equal(30, c.Abilities.Strength);
        Assert.Equal(1, c.Identity.Level);
        Assert.Equal(0, c.Combat.CurrentHp);
    }

    [Fact]
    public void WeaponAttack_CombinesWeaponEffectsAndModifiersIntoToHitAndDamage()
    {
        var c = new Character();
        c.Abilities.Strength = 16; // +3, PB +2
        var sword = new Weapon { Name = "Longsword", Types = "martial, melee", Damage = "1d8", DamageType = "slashing" };
        sword.Effects.Add(new WeaponEffect { Name = "Silver", Damage = "1" });
        sword.Effects.Add(new WeaponEffect { Name = "Flame", Damage = "2d6", DamageType = "fire" });
        c.Weapons.Add(sword);
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = "Simple, Martial weapons" });
        var attack = new Attack { WeaponId = sword.Id, Ability = null, Proficient = false };
        c.Attacks.Add(attack);

        Assert.Equal(5, CharacterRules.AttackBonus(c, attack)); // the weapon's ability and proficiency, not the attack's
        Assert.Equal("1d8 + 4 slashing + 2d6 fire", CharacterRules.DamageRoll(c, attack));
        Assert.Equal("2d6 fire", CharacterRules.AdditionalDamage(c, sword));

        var style = new Feature { Name = "Fighting style" };
        style.Modifiers.Add(new Modifier { Target = "attack.melee", Value = 1 });
        style.Modifiers.Add(new Modifier { Target = "damage.str", Value = 2 });
        c.Features.Add(style);
        Assert.Equal(6, CharacterRules.AttackBonus(c, attack));
        Assert.Equal("1d8 + 6 slashing + 2d6 fire", CharacterRules.DamageRoll(c, attack));

        var twoHanded = new Attack { WeaponId = sword.Id, Dice = "1d10", Damage = "ignored", Bonus = -1 };
        Assert.Equal(5, CharacterRules.AttackBonus(c, twoHanded));
        Assert.Equal("1d10 + 6 slashing + 2d6 fire", CharacterRules.DamageRoll(c, twoHanded));

        c.Weapons.Clear();
        Assert.Equal("ignored", CharacterRules.DamageRoll(c, twoHanded)); // orphaned: plain attack
    }

    [Fact]
    public void WondrousItem_AppliesOnlyWhileEquipped()
    {
        var c = new Character();
        var cloak = new Item { Name = "Cloak of Protection" };
        cloak.Modifiers.Add(new Modifier { Target = StatTargets.ArmorClass, Value = 1 });
        cloak.Modifiers.Add(new Modifier { Target = StatTargets.AllSaves, Value = 1 });
        c.WondrousItems.Add(cloak);
        Assert.Equal(10, CharacterRules.ArmorClass(c));

        cloak.Equipped = true;
        Assert.Equal(11, CharacterRules.ArmorClass(c));
        Assert.Equal(1, CharacterRules.SavingThrow(c, Ability.Wisdom));

        cloak.Equipped = false;
        Assert.Equal(0, CharacterRules.SavingThrow(c, Ability.Wisdom));
    }

    [Theory]
    [InlineData("Martial", true)]
    [InlineData("Simple, Martial", true)]
    [InlineData("martial weapons", true)]
    [InlineData("Longswords", true)]
    [InlineData("Simple", false)]
    [InlineData("Shortswords", false)]
    public void WeaponProficiency_MatchesNameOrType(string proficiency, bool expected)
    {
        var c = new Character();
        var sword = new Weapon { Name = "Longsword", Types = "martial, melee" };
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = proficiency });
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Armor, Name = "Longsword" }); // wrong category: ignored
        Assert.Equal(expected, CharacterRules.IsProficient(c, sword));
    }

    [Fact]
    public void WeaponTypes_SplitOnCommasOnly()
    {
        var c = new Character();
        var bow = new Weapon { Name = "Kaya's Bow", Types = "Simple, Ranged, Light crossbow" };
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = "Light crossbows" });
        Assert.True(CharacterRules.IsProficient(c, bow));

        c.Proficiencies[0].Name = "Light";
        Assert.False(CharacterRules.IsProficient(c, bow));
    }

    [Fact]
    public void WeaponTypeOptions_ListCustomTypesInUseOnce()
    {
        var c = new Character();
        c.Weapons.Add(new Weapon { Types = "Martial, Melee, Katana" });
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = "katanas, Longswords, Firearms" });
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Tool, Name = "Thieves' tools" });

        var options = CharacterRules.WeaponTypeOptions(c);

        Assert.Equal(["Simple", "Martial", "Melee", "Ranged", "Firearms", "Katana"], options.Take(6));
        Assert.Single(options, o => o.Equals("Longsword", StringComparison.OrdinalIgnoreCase)); // plural matched the standard one
        Assert.DoesNotContain("Thieves' tools", options);
    }
}
