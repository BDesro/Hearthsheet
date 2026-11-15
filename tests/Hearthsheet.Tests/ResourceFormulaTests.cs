using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Tests;

public class ResourceFormulaTests
{
    private static Character Multiclass()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 11 };
        c.Identity.Classes.Add(new ClassLevel { Name = "Wizard", Level = 2 });
        return c;
    }

    [Fact]
    public void Dice_and_number()
    {
        var r = ResourceFormula.TryResolve(new Character(), "1d10 + 3")!;
        Assert.Equal([new DiceTerm(1, 10)], r.Dice);
        Assert.Equal(3, r.Flat);
        Assert.Equal("1d10 + 3", r.Description);
    }

    [Fact]
    public void Fighter_level_on_multiclass()
    {
        var r = ResourceFormula.TryResolve(Multiclass(), "1d10 + fighter level")!;
        Assert.Equal(11, r.Flat);
        Assert.Equal("1d10 + 11 (fighter level)", r.Description);
    }

    [Fact]
    public void Hyphenated_class_name()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Spell-Sword", Level = 4 };
        var r = ResourceFormula.TryResolve(c, "1d6 + spell-sword level - 1")!;
        Assert.Equal(3, r.Flat);
        Assert.Equal("1d6 + 4 (spell-sword level) − 1", r.Description);
    }

    [Fact]
    public void Hyphen_before_a_number_still_subtracts() =>
        Assert.Equal(-1, ResourceFormula.TryResolve(new Character(), "1d6-1")!.Flat);

    [Fact]
    public void Class_stored_with_double_space_matches()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Blood  Hunter", Level = 5 };
        Assert.Equal(5, ResourceFormula.TryResolve(c, "blood hunter level")!.Flat);
    }

    [Fact]
    public void Class_level_is_case_and_space_insensitive() =>
        Assert.Equal(11, ResourceFormula.TryResolve(Multiclass(), "  1d10+  FIGHTER   Level ")!.Flat);

    [Fact]
    public void Class_name_with_spaces()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Blood Hunter", Level = 5 };
        Assert.Equal(5, ResourceFormula.TryResolve(c, "blood hunter level")!.Flat);
    }

    [Fact]
    public void Several_entries_of_one_class_add_up()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 3 };
        c.Identity.Classes.Add(new ClassLevel { Name = "Fighter", Level = 2 });
        Assert.Equal(5, ResourceFormula.TryResolve(c, "fighter level")!.Flat);
    }

    [Fact]
    public void Missing_class_counts_zero()
    {
        var r = ResourceFormula.TryResolve(Multiclass(), "1d10 + paladin level")!;
        Assert.Equal(0, r.Flat);
        Assert.Equal("1d10 + 0 (no paladin levels)", r.Description);
    }

    [Fact]
    public void Total_level_and_proficiency()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Level = 13 };
        Assert.Equal(18, ResourceFormula.TryResolve(c, "level + proficiency")!.Flat);
    }

    [Fact]
    public void Ability_short_and_long()
    {
        var c = new Character();
        c.Abilities.Constitution = 16;
        Assert.Equal(3, ResourceFormula.TryResolve(c, "con")!.Flat);
        Assert.Equal(3, ResourceFormula.TryResolve(c, "Constitution")!.Flat);
        Assert.Equal("2d4 + 3 (CON)", ResourceFormula.TryResolve(c, "2d4 + CON")!.Description);
    }

    [Fact]
    public void Negative_modifier_shows_minus()
    {
        var c = new Character();
        c.Abilities.Wisdom = 8;
        var r = ResourceFormula.TryResolve(c, "1d8 + wis")!;
        Assert.Equal(-1, r.Flat);
        Assert.Equal("1d8 − 1 (WIS)", r.Description);
    }

    [Theory]
    [InlineData("2d6 - 1")]
    [InlineData("2d6 − 1")]
    public void Subtraction_and_unicode_minus(string formula) =>
        Assert.Equal(-1, ResourceFormula.TryResolve(new Character(), formula)!.Flat);

    [Fact]
    public void Leading_sign()
    {
        Assert.NotNull(ResourceFormula.TryResolve(new Character(), "+1d6"));
        Assert.Equal(-2, ResourceFormula.TryResolve(new Character(), "-2")!.Flat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1d10 +")]
    [InlineData("1d10 ++ 2")]
    [InlineData("1d7")]
    [InlineData("0d6")]
    [InlineData("101d6")]
    [InlineData("１d10")]
    [InlineData("99999999999")]
    [InlineData("1000")]
    [InlineData("heal 1d10")]
    [InlineData("1d10 fire")]
    [InlineData("- 1d6")]
    [InlineData("(1d6)")]
    [InlineData("1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1")]
    public void Unreadable(string formula)
    {
        Assert.Null(ResourceFormula.TryResolve(Multiclass(), formula));
        Assert.False(ResourceFormula.IsReadable(formula));
    }

    [Fact]
    public void Readable_without_character() =>
        Assert.True(ResourceFormula.IsReadable("1d10 + fighter level"));
}
