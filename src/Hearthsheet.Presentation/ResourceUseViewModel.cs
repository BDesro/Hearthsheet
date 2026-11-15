using System.Globalization;
using System.Windows.Input;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Presentation;

/// <summary>
/// The resource-roll dialog's state: type in physical dice (grouped by die size) or let the app roll what is empty.
/// Nothing here touches the character; the caller applies <see cref="Total"/> when the user chooses Use.
/// </summary>
public sealed class ResourceUseViewModel : Observable
{
    private readonly Random _random;
    private readonly List<DamageBox> _boxes = [];
    private readonly int _flat;
    private readonly ResourceResult _result;

    public ResourceUseViewModel(Character c, Resource r, ResolvedRoll roll, Random random)
    {
        _random = random;
        _flat = roll.Flat;
        _result = r.Result;
        Name = r.Name;
        UsesText = $"{r.Current} / {r.Maximum} left";
        FormulaText = roll.Description;

        foreach (var group in roll.Dice.GroupBy(d => d.Sides))
            _boxes.Add(new DamageBox(new DamagePart("", [new DiceTerm(group.Sum(d => d.Count), group.Key)], 0), Changed, ""));

        RollForMe = new RelayCommand(DoRollForMe);
    }

    public string Name { get; }
    public string UsesText { get; }
    public string FormulaText { get; }
    public IReadOnlyList<DamageBox> Boxes => _boxes;
    public ICommand RollForMe { get; }

    public int? Total => _boxes.All(b => b.Value is not null) ? _boxes.Sum(b => b.Value!.Value) + _flat : null;
    public bool CanUse => Total is not null;

    public string ResultText
    {
        get
        {
            if (Total is not { } total) return "";
            var applied = Math.Max(0, total);
            return _result switch
            {
                ResourceResult.Heal => $"Heal {applied}",
                ResourceResult.TemporaryHp => $"Gain {applied} temporary HP",
                _ => $"Total {total}",
            };
        }
    }

    /// <summary>"Total 18 → Heal 18" (just "Total N" for Just show); empty until every box is valid.</summary>
    public string SummaryText => Total is not { } total ? ""
        : _result == ResourceResult.JustShow ? ResultText : $"Total {total} → {ResultText}";

    private void Changed()
    {
        Raise(nameof(SummaryText));
        Raise(nameof(Total));
        Raise(nameof(ResultText));
        Raise(nameof(CanUse));
    }

    private void DoRollForMe()
    {
        foreach (var box in _boxes.Where(b => string.IsNullOrWhiteSpace(b.Input)))
            box.Input = AttackMath.RollDice(box.Part, _random).ToString(CultureInfo.InvariantCulture);
    }
}
