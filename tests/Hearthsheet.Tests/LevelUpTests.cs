using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Tests;

public class LevelUpTests
{
    private static Character Fighter11()
    {
        var c = new Character();
        var f = c.Identity.Classes[0];
        f.Name = "Fighter";
        f.Level = 11;
        f.HitDie = 10;
        f.HitDiceRemaining = 11;
        c.Abilities.Strength = 16;
        c.Abilities.Intelligence = 10;
        c.Abilities.Constitution = 14;
        c.Combat.MaxHp = 100;
        c.Combat.CurrentHp = 100;
        return c;
    }

    private static Character Wizard(int level, int intelligence = 16)
    {
        var c = new Character();
        var w = c.Identity.Classes[0];
        w.Name = "Wizard";
        w.Level = level;
        w.HitDie = 6;
        w.HitDiceRemaining = level;
        w.Caster = CasterType.Full;
        w.SpellcastingAbility = Ability.Intelligence;
        c.Abilities.Intelligence = intelligence;
        return c;
    }

    [Fact]
    public void LevelUp_FighterIntoBloodHunter()
    {
        var c = Fighter11();
        var report = CharacterFactory.LevelUp(c, "Blood Hunter", new());

        var bh = c.Identity.Classes[1];
        Assert.Equal(("Blood Hunter", 1, 10, 1), (bh.Name, bh.Level, bh.HitDie, bh.HitDiceRemaining));
        Assert.Equal(108, c.Combat.MaxHp);
        Assert.Equal(108, c.Combat.CurrentHp);
        Assert.Single(report.Warnings);
        Assert.Equal("Blood Hunter multiclassing needs (Intelligence 13 and Strength 13) or (Intelligence 13 and Dexterity 13).", report.Warnings[0]);
        foreach (var (cat, name) in SrdCatalog.FindClass("Blood Hunter")!.MulticlassProficiencies)
            Assert.True(CharacterRules.HasProficiency(c, cat, name), name);
        Assert.Equal(6, c.Proficiencies.Count);
        Assert.Contains("Add Blood Hunter 1 features by hand.", report.NeedsAttention);
    }

    [Fact]
    public void LevelUp_ExistingClass_IncrementsAndChecksNoPrereqs()
    {
        var c = Fighter11();
        var report = CharacterFactory.LevelUp(c, "fighter", new());
        Assert.Empty(report.Warnings);
        Assert.Single(c.Identity.Classes);
        Assert.Equal((12, 12), (c.Identity.Classes[0].Level, c.Identity.Classes[0].HitDiceRemaining));
    }

    [Fact]
    public void LevelUp_PrereqCheckedForExistingClassesToo()
    {
        var c = Wizard(3, intelligence: 12);
        c.Abilities.Strength = 14;
        var report = CharacterFactory.LevelUp(c, "Fighter", new());
        var warning = Assert.Single(report.Warnings);
        Assert.Contains("Wizard", warning);
        Assert.Contains("Intelligence", warning);
    }

    [Fact]
    public void LevelUp_DoesNotDuplicateProficiencies()
    {
        var c = Fighter11();
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = "Simple, Martial" });
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Armor, Name = "light Armor" });
        CharacterFactory.LevelUp(c, "Blood Hunter", new());
        Assert.DoesNotContain(c.Proficiencies, p => p.Name is "Simple weapons" or "Martial weapons" or "Light armor");
        Assert.Contains(c.Proficiencies, p => p.Name == "Medium armor");
    }

    [Fact]
    public void LevelUp_SkillChoiceGoesToNeedsAttention()
    {
        var c = Fighter11();
        c.Abilities.Dexterity = 14;
        var report = CharacterFactory.LevelUp(c, "Rogue", new());
        // The catalog (spec table) words the Rogue choice "one class skill".
        Assert.Contains("Choose one class skill (Rogue multiclass).", report.NeedsAttention);
    }

    [Fact]
    public void LevelUp_RollUsed_AndValidated()
    {
        var c = Fighter11();
        CharacterFactory.LevelUp(c, "Fighter", new(Roll: 9));
        Assert.Equal(111, c.Combat.MaxHp);

        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterFactory.LevelUp(c, "Fighter", new(Roll: 11)));
        Assert.Equal(12, c.Identity.Classes[0].Level);

        var weak = Fighter11();
        weak.Abilities.Constitution = 4; // -3
        CharacterFactory.LevelUp(weak, "Fighter", new(Roll: 1));
        Assert.Equal(101, weak.Combat.MaxHp);
    }

    [Fact]
    public void LevelUp_AtTwenty_Refused()
    {
        var c = Fighter11();
        c.Identity.Classes[0].Level = 20;
        var report = CharacterFactory.LevelUp(c, "Fighter", new());
        Assert.False(report.Completed);
        Assert.Equal(20, c.Identity.Classes[0].Level);
        Assert.Equal(100, c.Combat.MaxHp);
        Assert.Contains(report.NeedsAttention, n => n.Contains("level 20"));
    }

    [Fact]
    public void LevelUp_CustomClass()
    {
        var c = Fighter11();
        var report = CharacterFactory.LevelUp(c, "Witch", new(HitDie: 6, Caster: CasterType.Full, Ability: Ability.Charisma));
        var witch = c.Identity.Classes[1];
        Assert.Equal(("Witch", 1, 6, CasterType.Full, (Ability?)Ability.Charisma), (witch.Name, witch.Level, witch.HitDie, witch.Caster, witch.SpellcastingAbility));
        Assert.Empty(report.Warnings);
        Assert.Empty(c.Proficiencies);
        Assert.Contains("Add Witch proficiencies by hand.", report.NeedsAttention);
    }

    [Fact]
    public void LevelUp_NonCasterKeepsHandEnteredSlots()
    {
        var c = Fighter11();
        c.Spellcasting.SlotLevel(1).Maximum = 3;
        var p = c.Spellcasting.PactSlots;
        (p.Level, p.Maximum, p.Expended) = (2, 2, 1);
        CharacterFactory.LevelUp(c, "Blood Hunter", new());
        Assert.Equal(3, c.Spellcasting.SlotLevel(1).Maximum);
        Assert.Equal((2, 2, 1), (p.Level, p.Maximum, p.Expended));
    }

    [Fact]
    public void LevelUp_CasterRecomputesSlotsKeepsExpended()
    {
        var c = Wizard(4);
        (c.Spellcasting.SlotLevel(1).Maximum, c.Spellcasting.SlotLevel(1).Expended) = (4, 2);
        (c.Spellcasting.SlotLevel(2).Maximum, c.Spellcasting.SlotLevel(2).Expended) = (3, 3);
        CharacterFactory.LevelUp(c, "Wizard", new());
        Assert.Equal(2, c.Spellcasting.SlotLevel(3).Maximum);
        Assert.Equal(2, c.Spellcasting.SlotLevel(1).Expended);
        Assert.Equal(3, c.Spellcasting.SlotLevel(2).Expended);
    }

    [Fact]
    public void MulticlassWarnings_MatchesLevelUp()
    {
        var c = Fighter11();
        var live = CharacterFactory.MulticlassWarnings(c, "Blood Hunter");
        Assert.Single(live);
        Assert.Empty(CharacterFactory.MulticlassWarnings(c, "fighter"));
        Assert.Empty(CharacterFactory.MulticlassWarnings(new Character(), "Wizard"));
        Assert.Equal(live, CharacterFactory.LevelUp(c, "Blood Hunter", new()).Warnings);
    }

    [Fact]
    public void ApplyClassDefaults_MultiEntry()
    {
        var c = Fighter11();
        c.Abilities.Constitution = 14;
        c.Identity.Classes[0].Subclass = "Eldritch Knight";
        c.Identity.Classes.Add(new ClassLevel { Name = "Blood Hunter", Level = 1, HitDie = 10, HitDiceRemaining = 0 });
        c.Identity.Classes[0].HitDiceRemaining = 3;

        CharacterFactory.ApplyClassDefaults(c);

        Assert.Equal([Ability.Strength, Ability.Constitution], c.SavingThrows.Where(s => s.Proficient).Select(s => s.Ability).Order());
        Assert.Equal(100, c.Combat.MaxHp);
        Assert.All(c.Identity.Classes, e => Assert.Equal(e.Level, e.HitDiceRemaining));
        Assert.Equal(CasterType.Third, c.Identity.Classes[0].Caster);
        Assert.Equal([4, 3], c.Spellcasting.Slots.Select(s => s.Maximum).Take(2));
        Assert.Equal(0, c.Spellcasting.SlotLevel(3).Maximum);
    }

    [Fact]
    public void ApplyClassDefaults_BuiltInClassOverwritesStoredValues()
    {
        var c = new Character();
        var e = c.Identity.Classes[0];
        (e.Name, e.Level, e.HitDie, e.Caster, e.SpellcastingAbility) = ("Fighter", 5, 6, CasterType.Full, Ability.Intelligence);
        CharacterFactory.ApplyClassDefaults(c);
        Assert.Equal((10, CasterType.None, null), (e.HitDie, e.Caster, e.SpellcastingAbility));
        Assert.All(c.Spellcasting.Slots, s => Assert.Equal(0, s.Maximum) );
    }

    [Fact]
    public void ApplyClassDefaults_EldritchKnightStaysThird()
    {
        var c = Fighter11();
        c.Identity.Classes[0].Level = 7;
        c.Identity.Classes[0].Subclass = "Eldritch Knight";
        c.Identity.Classes[0].Caster = CasterType.None;
        c.Identity.Classes[0].SpellcastingAbility = null;
        CharacterFactory.ApplyClassDefaults(c);
        Assert.Equal((CasterType.Third, Ability.Intelligence), (c.Identity.Classes[0].Caster, c.Identity.Classes[0].SpellcastingAbility!.Value));
        Assert.Equal([4, 2], c.Spellcasting.Slots.Select(s => s.Maximum).Take(2));
    }

    [Fact]
    public void ApplyClassDefaults_CustomEntryKeepsStoredValues()
    {
        var c = new Character();
        var e = c.Identity.Classes[0];
        (e.Name, e.Level, e.HitDie, e.Caster, e.SpellcastingAbility) = ("Witch", 3, 6, CasterType.Full, Ability.Charisma);
        CharacterFactory.ApplyClassDefaults(c);
        Assert.Equal((6, CasterType.Full, Ability.Charisma), (e.HitDie, e.Caster, e.SpellcastingAbility!.Value));
    }

    [Fact]
    public void LevelUp_HpUsesRawConstitution()
    {
        var c = Fighter11(); // CON +2
        var belt = new Effect { Name = "Belt" };
        belt.Modifiers.Add(new Modifier { Target = StatTargets.AbilityScore(Ability.Constitution), Kind = ModifierKind.Set, Value = 19 });
        c.Effects.Add(belt);
        var hp = c.Combat.MaxHp;
        CharacterFactory.LevelUp(c, "Fighter", new());
        Assert.Equal(hp + 6 + 2, c.Combat.MaxHp);
    }

    [Fact]
    public void ApplyClassDefaults_CustomFirstClassAllowed()
    {
        var c = new Character();
        var e = c.Identity.Classes[0];
        (e.Name, e.Level, e.HitDie) = ("Witch", 3, 6);
        c.Abilities.Constitution = 10;
        CharacterFactory.ApplyClassDefaults(c);
        Assert.Equal(6, e.HitDie);
        Assert.Equal(6 + 2 * 4, c.Combat.MaxHp);
        Assert.Equal(3, e.HitDiceRemaining);
    }

    [Fact]
    public void LevelUp_CustomHitDieIsClamped()
    {
        var c = Fighter11(); // CON +2
        CharacterFactory.LevelUp(c, "Witch", new(HitDie: 100));
        Assert.Equal(20, c.Identity.Classes[1].HitDie);
        Assert.Equal(100 + 11 + 2, c.Combat.MaxHp);
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterFactory.LevelUp(c, "Witch", new(Roll: 21)));
    }

    [Fact]
    public void LevelUp_BlankEntryIsKeptAndNewClassAppended()
    {
        var c = new Character();
        c.Identity.Classes[0].Level = 3;
        var report = CharacterFactory.LevelUp(c, "Fighter", new());
        Assert.Equal(3, c.Identity.Classes[0].Level);
        Assert.Equal(("Fighter", 1), (c.Identity.Classes[1].Name, c.Identity.Classes[1].Level));
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void LevelUp_SlotRecomputeClampsExpended()
    {
        var c = new Character();
        c.Identity.Classes[0].Name = "Paladin";
        c.Identity.Classes[0].Level = 3;
        c.Identity.Classes[0].Caster = CasterType.Half;
        c.Abilities.Dexterity = 14;
        c.Abilities.Wisdom = 14;
        (c.Spellcasting.SlotLevel(1).Maximum, c.Spellcasting.SlotLevel(1).Expended) = (3, 3);
        CharacterFactory.LevelUp(c, "Ranger", new());
        Assert.Equal((2, 2), (c.Spellcasting.SlotLevel(1).Maximum, c.Spellcasting.SlotLevel(1).Expended));
    }
}
