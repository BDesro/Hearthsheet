using Hearthsheet.Core.Content;
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
    public void ProficiencyBonus_UsesTotalLevel()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 11 };
        c.Identity.Classes.Add(new ClassLevel { Name = "Blood Hunter", Level = 1 });
        Assert.Equal(12, CharacterRules.TotalLevel(c));
        Assert.Equal(4, CharacterRules.ProficiencyBonus(c));
    }

    [Fact]
    public void HitDicePools_GroupByDieSize()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", HitDie = 10, Level = 11, HitDiceRemaining = 9 };
        c.Identity.Classes.Add(new ClassLevel { Name = "Rogue", HitDie = 8, Level = 1, HitDiceRemaining = 1 });
        c.Identity.Classes.Add(new ClassLevel { Name = "Paladin", HitDie = 10, Level = 2, HitDiceRemaining = 2 });
        Assert.Equal([new HitDicePool(10, 11, 13), new HitDicePool(8, 1, 1)], CharacterRules.HitDicePools(c));
    }

    [Fact]
    public void ClassSummary_SingleAndMulti()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 11, Subclass = "Echo Knight" };
        Assert.Equal("Fighter", CharacterRules.ClassSummary(c));
        Assert.Equal("Fighter 11", CharacterRules.ClassAndLevelText(c));
        c.Identity.Classes.Add(new ClassLevel { Name = "Blood Hunter", Level = 1 });
        Assert.Equal("Fighter 11 (Echo Knight) / Blood Hunter 1", CharacterRules.ClassSummary(c));
        Assert.Equal("Fighter 11 (Echo Knight) / Blood Hunter 1", CharacterRules.ClassAndLevelText(c));
    }

    [Fact]
    public void Skill_CombinesAbilityProficiencyExpertiseAndBonus()
    {
        var c = new Character();
        c.Identity.Classes[0].Level = 5; // PB +3
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
        c.Identity.Classes[0].Level = 5; // PB 3 → half = 1
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
        c.Identity.Classes[0].Level = 9; // PB 4
        c.Abilities.Dexterity = 14;
        c.Abilities.Wisdom = 12;
        c.Abilities.Intelligence = 18;
        c.Combat.InitiativeBonus = 1;
        c.SkillEntry(Skill.Perception).Proficiency = ProficiencyLevel.Proficient;
        c.Identity.Classes[0].SpellcastingAbility = Ability.Intelligence;

        Assert.Equal(3, CharacterRules.Initiative(c));
        Assert.Equal(15, CharacterRules.PassivePerception(c));
        Assert.Equal(16, CharacterRules.SpellSaveDc(c));
        Assert.Equal(8, CharacterRules.SpellAttackBonus(c));

        c.Identity.Classes[0].SpellcastingAbility = null;
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
        c.Identity.Classes[0].Level = 0;
        c.Combat.CurrentHp = -5;
        Assert.Equal(30, c.Abilities.Strength);
        Assert.Equal(1, c.Identity.Classes[0].Level);
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

    private static ClassLevel[] Entries(string spec) => [.. spec.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p =>
    {
        var parts = p.Split(':');
        return new ClassLevel { Name = parts[0], Caster = Enum.Parse<CasterType>(parts[1]), Level = int.Parse(parts[2]) };
    })];

    [Theory]
    [InlineData("Paladin:Half:5", "4,2")]
    [InlineData("Fighter:Third:7", "4,2")]
    [InlineData("Fighter:Third:2", "")]
    [InlineData("Wizard:Full:5", "4,3,2")]
    [InlineData("Paladin:Half:2,Sorcerer:Full:3", "4,3")]
    [InlineData("Warlock:Pact:3,Sorcerer:Full:2", "3")]
    [InlineData("Fighter:None:11,BloodHunter:None:1", "")]
    public void SpellSlots_SingleAndMulticlass(string classes, string expected)
    {
        var want = new int[9];
        foreach (var (n, i) in expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((s, i) => (int.Parse(s), i))) want[i] = n;
        Assert.Equal(want, SrdCatalog.SpellSlots(Entries(classes)));
    }

    [Fact]
    public void SpellDc_PerClass()
    {
        var c = new Character();
        c.Abilities.Intelligence = 18;
        c.Abilities.Wisdom = 14;
        c.Identity.Classes[0] = new ClassLevel { Name = "Wizard", Level = 5, SpellcastingAbility = Ability.Intelligence };
        c.Identity.Classes.Add(new ClassLevel { Name = "Cleric", Level = 3, SpellcastingAbility = Ability.Wisdom });
        var cleric = new Spell { Class = "cleric" };
        var plain = new Spell();
        Assert.Equal(13, CharacterRules.SpellSaveDc(c, cleric));
        Assert.Equal(15, CharacterRules.SpellSaveDc(c, plain));
        Assert.Equal(15, CharacterRules.SpellSaveDc(c));
        Assert.Equal(5, CharacterRules.SpellAttackBonus(c, cleric));

        c.Items.Add(new Item { Name = "Focus", Equipped = true, Modifiers = { new Modifier { Target = StatTargets.SpellDc, Value = 1 } } });
        Assert.Equal(14, CharacterRules.SpellSaveDc(c, cleric));
        Assert.Equal(16, CharacterRules.SpellSaveDc(c, plain));
    }

    [Fact]
    public void Catalog_BloodHunterFacts()
    {
        var bh = SrdCatalog.FindClass("blood hunter")!;
        Assert.Equal(10, bh.HitDie);
        Assert.Equal([Ability.Dexterity, Ability.Intelligence], bh.SavingThrows);
        Assert.Equal(CasterType.None, bh.Caster);
        Assert.Equal(6, bh.MulticlassProficiencies.Length);
        Assert.Contains((ProficiencyCategory.Tool, "Alchemist's supplies"), bh.MulticlassProficiencies);
    }

    [Fact]
    public void Catalog_EveryClassHasPrerequisites()
    {
        Assert.Equal(13, SrdCatalog.Classes.Count);
        Assert.All(SrdCatalog.Classes, c => Assert.NotEmpty(c.MulticlassPrerequisites));
    }

    [Fact]
    public void Catalog_MonkNeedsDexAndWis()
    {
        var alt = Assert.Single(SrdCatalog.FindClass("Monk")!.MulticlassPrerequisites);
        Assert.Equal([(Ability.Dexterity, 13), (Ability.Wisdom, 13)], alt);
    }

    [Theory]
    [InlineData("Eldritch Knight", CasterType.None, CasterType.Third)]
    [InlineData("  arcane trickster ", CasterType.None, CasterType.Third)]
    [InlineData("Eldritch Knight", CasterType.Half, CasterType.Half)]
    [InlineData("Champion", CasterType.None, CasterType.None)]
    public void Subclass_EldritchKnight_MakesThirdCaster(string subclass, CasterType start, CasterType want)
    {
        var e = new ClassLevel { Name = "Fighter", Caster = start };
        e.Subclass = subclass;
        Assert.Equal(want, e.Caster);
        Assert.Equal(want == CasterType.Third ? Ability.Intelligence : null, e.SpellcastingAbility);
    }
}
