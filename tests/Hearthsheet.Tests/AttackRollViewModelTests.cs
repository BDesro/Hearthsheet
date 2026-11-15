using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

public class AttackRollViewModelTests
{
    // Strength 16 (+3), proficient: +5 to hit. Longsword 1d8 + 3 slashing, plus 2d6 fire when withFire.
    private static (Character C, Attack A) Sword(bool withFire = true, string dice = "1d8")
    {
        var c = new Character();
        c.Abilities.Strength = 16;
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Weapon, Name = "Simple, Martial weapons" });
        var w = new Weapon { Name = "Longsword", Types = "martial, melee", Damage = dice, DamageType = "slashing" };
        if (withFire) w.Effects.Add(new WeaponEffect { Name = "Flame", Damage = "2d6", DamageType = "fire" });
        c.Weapons.Add(w);
        var a = new Attack { WeaponId = w.Id };
        c.Attacks.Add(a);
        return (c, a);
    }

    private static AttackRollViewModel Roll(bool withFire = true, string dice = "1d8")
    {
        var (c, a) = Sword(withFire, dice);
        return new AttackRollViewModel(c, a, new Random(1));
    }

    [Fact]
    public void NaturalTwenty_TicksCritical_AndShowsIt()
    {
        var vm = Roll();
        vm.D20First = "20";
        Assert.True(vm.IsCritical);
        Assert.StartsWith("Natural 20", vm.ToHitText);
        Assert.Equal("Natural 20: critical hit, 25 to hit", vm.ToHitText);
    }

    [Fact]
    public void AutomaticCritical_ClearsWhenTheD20Changes()
    {
        var vm = Roll();
        vm.D20First = "20";
        vm.D20First = "12";
        Assert.False(vm.IsCritical);
        Assert.Equal("12 + 5 = 17 to hit", vm.ToHitText);
    }

    [Fact]
    public void ManualUntick_Sticks()
    {
        var vm = Roll();
        vm.D20First = "20";
        vm.IsCritical = false;
        vm.D20First = "20";
        Assert.False(vm.IsCritical);
    }

    [Fact]
    public void ManualTick_IsNotClearedByTheD20()
    {
        var vm = Roll();
        vm.D20First = "20";
        vm.IsCritical = true; // now manual
        vm.D20First = "12";
        Assert.True(vm.IsCritical);
    }

    [Fact]
    public void ManualTickBeforeTwenty_SurvivesLeavingTwenty()
    {
        var vm = Roll();
        vm.IsCritical = true;
        vm.D20First = "20";
        vm.D20First = "12";
        Assert.True(vm.IsCritical);
    }

    [Fact]
    public void ManualUntick_SurvivesLeavingAndReturningToTwenty()
    {
        var vm = Roll();
        vm.D20First = "20";
        vm.IsCritical = false;
        vm.D20First = "12";
        vm.D20First = "20";
        Assert.False(vm.IsCritical);
    }

    [Fact]
    public void ClearingABox_RaisesErrorAndClearsIt()
    {
        var vm = Roll();
        var box = vm.Boxes[0];
        box.Input = "abc";
        Assert.NotNull(box.Error);
        var raised = new List<string?>();
        box.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        box.Input = "";
        Assert.Null(box.Error);
        Assert.Contains(nameof(DamageBox.Error), raised);
    }

    [Fact]
    public void NaturalOne_IsAMiss()
    {
        var vm = Roll();
        vm.D20First = "1";
        Assert.Equal("Natural 1: miss", vm.ToHitText);
    }

    [Fact]
    public void ModeSwitch_NeedsBothD20s_ThenShowsTotalAgain()
    {
        var vm = Roll();
        vm.D20First = "14";
        vm.Mode = RollMode.Advantage;
        Assert.True(vm.HasSecondD20);
        Assert.Equal("", vm.ToHitText);
        vm.D20Second = "18";
        Assert.Equal("18 + 5 = 23 to hit", vm.ToHitText);
        vm.Mode = RollMode.Disadvantage;
        Assert.Equal("14 + 5 = 19 to hit", vm.ToHitText);
        vm.Mode = RollMode.Normal;
        Assert.False(vm.HasSecondD20);
        Assert.Equal("14 + 5 = 19 to hit", vm.ToHitText);
    }

    [Fact]
    public void JunkInput_IsNullValueWithError_AndLeftOutOfTotals()
    {
        var vm = Roll();
        var fire = vm.Boxes.Single(b => b.Part.Type == "fire");

        fire.Input = "";
        Assert.Null(fire.Value);
        Assert.Null(fire.Error);
        foreach (var junk in new[] { "abc", "-3", "999" })
        {
            fire.Input = junk;
            Assert.Null(fire.Value);
            Assert.NotNull(fire.Error);
        }
        Assert.Contains("Fire 0", vm.TotalsText);
        fire.Input = "7";
        Assert.Equal(7, fire.Value);
        Assert.Null(fire.Error);
        Assert.Contains("Fire 7", vm.TotalsText);
    }

    [Fact]
    public void Critical_DoublesTheDice_AndUntickingFlagsAnOutOfRangeValue()
    {
        var vm = Roll();
        var fire = vm.Boxes.Single(b => b.Part.Type == "fire");
        Assert.Equal("2d6 fire", fire.Label);
        Assert.Equal(12, fire.Max);

        vm.IsCritical = true;
        Assert.Equal("4d6 fire", fire.Label);
        Assert.Equal(24, fire.Max);
        fire.Input = "20";
        Assert.Equal(20, fire.Value);
        Assert.Equal("2d8 + 3 slashing", vm.Boxes[0].Label);

        vm.IsCritical = false;
        Assert.Equal("20", fire.Input);
        Assert.Null(fire.Value);
        Assert.Equal("Between 2 and 12", fire.Error);
    }

    [Fact]
    public void RollForMe_FillsOnlyEmptyOrInvalidBoxes_D20Included()
    {
        var vm = Roll();
        var slash = vm.Boxes[0];
        var fire = vm.Boxes[1];
        slash.Input = "5";
        fire.Input = "99";
        vm.D20First = "";

        vm.RollForMe.Execute(null);

        Assert.Equal("5", slash.Input);
        Assert.NotNull(fire.Value);
        Assert.NotEqual("99", fire.Input);
        Assert.NotEqual("", vm.D20First);
        Assert.NotEqual("", vm.ToHitText);
        Assert.True(vm.IsComplete);
    }

    [Fact]
    public void RollForMe_KeepsATypedD20()
    {
        var vm = Roll();
        vm.D20First = "13";
        vm.RollForMe.Execute(null);
        Assert.Equal("13", vm.D20First);
    }

    [Fact]
    public void Fallback_ForFreeTextDamage()
    {
        var c = new Character();
        var a = new Attack { Name = "Fire Cone", Damage = "30-ft cone. Save DC 16. Half damage" };
        c.Attacks.Add(a);
        var vm = new AttackRollViewModel(c, a, new Random(1));

        Assert.False(vm.HasStructuredDamage);
        Assert.Equal("30-ft cone. Save DC 16. Half damage", vm.RawDamage);
        Assert.Empty(vm.Boxes);
        vm.FallbackInput = "24";
        Assert.Equal(24, vm.FallbackValue);
        Assert.Equal("Fire Cone: 24 damage", vm.LogLine);
    }

    [Theory]
    [InlineData("abc", null, "Enter a whole number")]
    [InlineData("10000", null, "Between 0 and 9999")]
    [InlineData("99999999999", null, "Between 0 and 9999")]
    [InlineData("24", 24, null)]
    [InlineData("", null, null)]
    public void FallbackError_AndValue(string input, int? value, string? error)
    {
        var c = new Character();
        var a = new Attack { Name = "Fire Cone", Damage = "30-ft cone. Save DC 16. Half damage" };
        c.Attacks.Add(a);
        var vm = new AttackRollViewModel(c, a, new Random(1));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FallbackInput = input;

        Assert.Equal(value, vm.FallbackValue);
        Assert.Equal(error, vm.FallbackError);
        if (input.Length > 0)
        {
            Assert.Contains(nameof(vm.FallbackError), raised);
            Assert.Contains(nameof(vm.FallbackValue), raised);
        }
    }

    [Fact]
    public void LogLine_AllForms()
    {
        var vm = Roll();
        vm.D20First = "14";
        vm.Boxes[0].Input = "6";
        vm.Boxes[1].Input = "5";
        Assert.Equal("Longsword: 19 to hit, 9 slashing + 5 fire", vm.LogLine);
        Assert.Equal("Slashing 9 · Fire 5 · Total 14", vm.TotalsText);
        Assert.True(vm.IsComplete);

        vm.Boxes[1].Input = "";
        Assert.Equal("Longsword: 19 to hit, 9 slashing + 0 fire (partial)", vm.LogLine);
        vm.Boxes[1].Input = "5";

        vm.D20First = "20";
        vm.Boxes[0].Input = "10";
        vm.Boxes[1].Input = "7";
        Assert.Equal("Longsword: 25 to hit (crit), 13 slashing + 7 fire", vm.LogLine);

        vm.D20First = "1";
        Assert.Equal("Longsword: natural 1 (miss)", vm.LogLine);

        vm.D20First = "";
        vm.IsCritical = false;
        vm.Boxes[0].Input = "6";
        vm.Boxes[1].Input = "5";
        Assert.Equal("Longsword: 9 slashing + 5 fire", vm.LogLine);
    }

    [Fact]
    public void LogLine_UntypedPrintsAsDamage_AndNoD20Form()
    {
        var c = new Character();
        var a = new Attack { Name = "Club", Damage = "1d6" };
        c.Attacks.Add(a);
        var vm = new AttackRollViewModel(c, a, new Random(1));
        vm.Boxes[0].Input = "4";
        Assert.Equal("Club: 4 damage", vm.LogLine);

        var fire = new Attack { Name = "Burning Hands", Damage = "4d6 fire" };
        var vm2 = new AttackRollViewModel(c, fire, new Random(1));
        vm2.Boxes[0].Input = "24";
        Assert.Equal("Burning Hands: 24 fire", vm2.LogLine);
    }

    [Fact]
    public void EmptyWeaponDice_MakeNoBoxAndNoTotal()
    {
        var (c, a) = Sword(withFire: false, dice: "");
        c.Abilities.Strength = 10; // no +3 either
        var vm = new AttackRollViewModel(c, a, new Random(1));
        Assert.Empty(vm.Boxes);
        Assert.True(vm.HasStructuredDamage);
        Assert.DoesNotContain("Slashing", vm.TotalsText);
    }

    [Fact]
    public void FlatOnlyPart_CountsInTotalsWithoutABox()
    {
        var c = new Character();
        var a = new Attack { Name = "Poke", Damage = "5 piercing" };
        c.Attacks.Add(a);
        var vm = new AttackRollViewModel(c, a, new Random(1));
        Assert.Empty(vm.Boxes);
        Assert.Equal("Piercing 5 · Total 5", vm.TotalsText);
        Assert.Equal("Poke: 5 piercing", vm.LogLine);
    }
}
