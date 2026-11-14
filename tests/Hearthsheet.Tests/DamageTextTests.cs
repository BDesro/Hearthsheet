using Hearthsheet.Core.Rules;

namespace Hearthsheet.Tests;

public class DamageTextTests
{
    private static string Dice(DamagePart p) => string.Join(",", p.Dice.Select(d => $"{d.Count}d{d.Sides}"));

    [Fact]
    public void SingleTypedGroup()
    {
        var parts = DamageText.TryParse("8d6 Fire")!;
        var p = Assert.Single(parts);
        Assert.Equal("Fire", p.Type);
        Assert.Equal("8d6", Dice(p));
        Assert.Equal(0, p.Flat);
    }

    [Fact]
    public void TwoTypedGroups()
    {
        var parts = DamageText.TryParse("1d10 + 3 piercing + 1d6 cold")!;
        Assert.Equal(2, parts.Count);
        Assert.Equal("piercing", parts[0].Type);
        Assert.Equal("1d10", Dice(parts[0]));
        Assert.Equal(3, parts[0].Flat);
        Assert.Equal("cold", parts[1].Type);
        Assert.Equal("1d6", Dice(parts[1]));
        Assert.Equal(0, parts[1].Flat);
    }

    [Fact]
    public void UntypedWithFlat()
    {
        var p = Assert.Single(DamageText.TryParse("2d4+2")!);
        Assert.Equal("", p.Type);
        Assert.Equal("2d4", Dice(p));
        Assert.Equal(2, p.Flat);
    }

    [Fact]
    public void BareDieMeansOne()
    {
        var p = Assert.Single(DamageText.TryParse("d20")!);
        Assert.Equal("1d20", Dice(p));
    }

    [Fact]
    public void SameTypeMergesIgnoringCase()
    {
        var p = Assert.Single(DamageText.TryParse("1d8 + 1d4 slashing + 2 Slashing")!);
        Assert.Equal("slashing", p.Type);
        Assert.Equal("1d8,1d4", Dice(p));
        Assert.Equal(2, p.Flat);
    }

    [Fact]
    public void UnicodeMinusIsNegativeFlat()
    {
        var p = Assert.Single(DamageText.TryParse("1d6 − 1 fire")!);
        Assert.Equal("fire", p.Type);
        Assert.Equal("1d6", Dice(p));
        Assert.Equal(-1, p.Flat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyIsEmptyList(string text) => Assert.Empty(DamageText.TryParse(text)!);

    [Fact]
    public void NullIsEmptyList() => Assert.Empty(DamageText.TryParse(null)!);

    [Theory]
    [InlineData("30-ft cone. Save DC 16")]
    [InlineData("1d7")]
    [InlineData("0d6")]
    [InlineData("101d6")]
    [InlineData("(1d6) fire")]
    [InlineData("2d6 fire damage")]
    [InlineData("1d6 - 1d4")]
    public void Unparseable(string text) => Assert.Null(DamageText.TryParse(text));
}
