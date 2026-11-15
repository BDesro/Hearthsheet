using Hearthsheet.Core.Rules;
using Xunit;

namespace Hearthsheet.Tests;

public class AttackMathTests
{
    private static DamagePart P(string type, int flat, params (int c, int s)[] dice) =>
        new(type, dice.Select(d => new DiceTerm(d.c, d.s)).ToList(), flat);

    [Theory]
    [InlineData(RollMode.Normal, 14, null, 14)]
    [InlineData(RollMode.Advantage, 3, 17, 17)]
    [InlineData(RollMode.Disadvantage, 3, 17, 3)]
    [InlineData(RollMode.Advantage, 3, null, null)]
    [InlineData(RollMode.Normal, null, 9, null)]
    public void D20Used(RollMode mode, int? first, int? second, int? expected) =>
        Assert.Equal(expected, AttackMath.D20Used(mode, first, second));

    [Fact]
    public void ForCriticalDoublesDiceCounts()
    {
        var result = AttackMath.ForCritical([P("fire", 3, (2, 6))]);
        var part = Assert.Single(result);
        Assert.Equal("fire", part.Type);
        Assert.Equal(3, part.Flat);
        Assert.Equal(new DiceTerm(4, 6), Assert.Single(part.Dice));
    }

    [Fact]
    public void RangeSumsCountAndMaxima() =>
        Assert.Equal((2, 12), AttackMath.Range(P("slashing", 0, (1, 8), (1, 4))));

    [Fact]
    public void RollDiceStaysWithinRange()
    {
        var part = P("slashing", 0, (1, 8), (1, 4));
        var (min, max) = AttackMath.Range(part);
        var rng = new Random(1);
        for (var i = 0; i < 100; i++)
            Assert.InRange(AttackMath.RollDice(part, rng), min, max);
    }

    [Fact]
    public void TotalsAddFlatAndTreatNullAsZero()
    {
        var parts = new[] { P("slashing", 4, (1, 8)), P("fire", 0, (2, 6)) };
        Assert.Equal([("slashing", 9), ("fire", 7)], AttackMath.Totals(parts, [5, 7]));
        Assert.Equal([("slashing", 4), ("fire", 7)], AttackMath.Totals(parts, [null, 7]));
    }

    [Fact]
    public void TotalsGroupSameTypeIgnoringCase()
    {
        var parts = new[] { P("Fire", 1), P("fire", 2), P("cold", 0) };
        Assert.Equal([("Fire", 3), ("cold", 0)], AttackMath.Totals(parts, [0, 0, 0]));
    }
}
