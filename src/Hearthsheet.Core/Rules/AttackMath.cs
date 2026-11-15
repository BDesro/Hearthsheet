namespace Hearthsheet.Core.Rules;

public enum RollMode { Normal, Advantage, Disadvantage }

/// <summary>Pure maths for the attack roller: d20 selection, crit doubling, dice ranges, rolling, totals.</summary>
public static class AttackMath
{
    /// <summary>The d20 that counts, or null when the mode still needs a roll that hasn't been made.</summary>
    public static int? D20Used(RollMode mode, int? first, int? second)
    {
        if (mode == RollMode.Normal) return first;
        if (first is null || second is null) return null;
        return mode == RollMode.Advantage ? Math.Max(first.Value, second.Value) : Math.Min(first.Value, second.Value);
    }

    public static IReadOnlyList<DamagePart> ForCritical(IReadOnlyList<DamagePart> parts) =>
        parts.Select(p => p with { Dice = p.Dice.Select(d => d with { Count = d.Count * 2 }).ToList() }).ToList();

    /// <summary>Dice only; the part's flat bonus is not included.</summary>
    public static (int Min, int Max) Range(DamagePart part) =>
        (part.Dice.Sum(d => d.Count), part.Dice.Sum(d => d.Count * d.Sides));

    /// <summary>Rolls every die (flat bonus not included).</summary>
    public static int RollDice(DamagePart part, Random random)
    {
        var sum = 0;
        foreach (var d in part.Dice)
            for (var i = 0; i < d.Count; i++) sum += random.Next(1, d.Sides + 1);
        return sum;
    }

    /// <summary>Per-type totals (flat + dice sum, null sum = 0), grouped ignoring case in first-seen order.</summary>
    public static IReadOnlyList<(string Type, int Total)> Totals(IReadOnlyList<DamagePart> parts, IReadOnlyList<int?> diceSums)
    {
        var order = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Count; i++)
        {
            var amount = parts[i].Flat + (i < diceSums.Count ? diceSums[i] ?? 0 : 0);
            if (totals.TryGetValue(parts[i].Type, out var running)) totals[parts[i].Type] = running + amount;
            else { order.Add(parts[i].Type); totals[parts[i].Type] = amount; }
        }
        return order.Select(t => (t, totals[t])).ToList();
    }
}
