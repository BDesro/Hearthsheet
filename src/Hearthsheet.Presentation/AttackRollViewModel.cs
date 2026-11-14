using System.Globalization;
using System.Windows.Input;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;

namespace Hearthsheet.Presentation;

/// <summary>One damage-dice entry: what to roll, what was typed, and whether it is a legal result.</summary>
public sealed class DamageBox : Observable
{
    private readonly Action _changed;

    internal DamageBox(DamagePart part, Action changed)
    {
        _changed = changed;
        Apply(part);
    }

    public DamagePart Part { get; private set; } = null!;
    public string Label { get; private set; } = "";
    public int Min { get; private set; }
    public int Max { get; private set; }
    public int? Value { get; private set; }
    public string? Error { get; private set; }

    public string Input
    {
        get;
        set
        {
            if (!Set(ref field, value ?? "")) return;
            Validate();
            _changed();
        }
    } = "";

    /// <summary>Swaps in the crit-doubled (or normal) part, keeping what was typed.</summary>
    internal void Apply(DamagePart part)
    {
        Part = part;
        (Min, Max) = AttackMath.Range(part);
        var dice = string.Join(" + ", part.Dice.Select(d => $"{d.Count}d{d.Sides}"));
        var flat = part.Flat > 0 ? $" + {part.Flat}" : part.Flat < 0 ? $" − {-part.Flat}" : "";
        Label = $"{dice}{flat} {(part.Type.Length > 0 ? part.Type : "damage")}";
        Validate();
        Raise(string.Empty);
    }

    private void Validate()
    {
        Value = null;
        Error = null;
        var text = Input.Trim();
        if (text.Length > 0)
        {
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) Error = "Enter a whole number";
            else if (n < Min || n > Max) Error = $"Between {Min} and {Max}";
            else Value = n;
        }
        Raise(nameof(Value));
        Raise(nameof(Error));
    }
}

/// <summary>
/// The attack-roll dialog's state: the player rolls physical dice and types them in, or lets the app roll
/// whatever is still empty. Nothing here touches the character; the caller logs <see cref="LogLine"/>.
/// </summary>
public sealed class AttackRollViewModel : Observable
{
    private readonly Random _random;
    private readonly IReadOnlyList<DamagePart>? _baseParts;
    private readonly List<DamageBox> _boxes = [];
    private readonly List<int> _boxPartIndex = [];
    private bool _critical;
    private bool _automaticCrit;
    private bool _manualCrit;
    private int? _lastUsed;

    public AttackRollViewModel(Character c, Attack attack, Random random)
    {
        _random = random;
        Name = new AttackRow(c, attack).Name;
        AttackBonus = CharacterRules.AttackBonus(c, attack);
        BonusText = CharacterRules.FormatModifier(AttackBonus);
        RawDamage = CharacterRules.DamageRoll(c, attack);

        // A part with no dice and no flat (a weapon with blank dice) has nothing to show or total.
        _baseParts = CharacterRules.DamageParts(c, attack)?.Where(p => p.Dice.Count > 0 || p.Flat != 0).ToList();
        if (_baseParts is not null)
            for (var i = 0; i < _baseParts.Count; i++)
                if (_baseParts[i].Dice.Count > 0)
                {
                    _boxes.Add(new DamageBox(_baseParts[i], Changed));
                    _boxPartIndex.Add(i);
                }

        RollForMe = new RelayCommand(DoRollForMe);
    }

    public string Name { get; }
    public int AttackBonus { get; }
    public string BonusText { get; }

    public RollMode Mode
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            Raise(nameof(HasSecondD20));
            Changed();
        }
    }

    public bool HasSecondD20 => Mode != RollMode.Normal;

    public string D20First { get; set { if (Set(ref field, value ?? "")) Changed(); } } = "";
    public string D20Second { get; set { if (Set(ref field, value ?? "")) Changed(); } } = "";

    public string? D20Error =>
        Invalid(D20First) || (HasSecondD20 && Invalid(D20Second)) ? "Between 1 and 20" : null;

    public bool IsCritical
    {
        get => _critical;
        set
        {
            _automaticCrit = false;
            _manualCrit = true; // a manual tick or untick is never changed by later d20 edits
            SetCritical(value);
        }
    }

    public IReadOnlyList<DamageBox> Boxes => _boxes;

    public bool HasStructuredDamage => _baseParts is not null;
    public string RawDamage { get; }
    public string FallbackInput { get; set { if (Set(ref field, value ?? "")) Changed(); } } = "";

    public int? FallbackValue =>
        int.TryParse(FallbackInput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 9999 ? n : null;

    public ICommand RollForMe { get; }

    public string ToHitText
    {
        get
        {
            if (D20Used() is not { } used) return "";
            if (used == 1) return "Natural 1: miss";
            var total = used + AttackBonus;
            return used == 20
                ? $"Natural 20: critical hit, {total} to hit"
                : $"{used} {(AttackBonus >= 0 ? "+" : "−")} {Math.Abs(AttackBonus)} = {total} to hit";
        }
    }

    public string TotalsText
    {
        get
        {
            var totals = Totals();
            return totals.Count == 0 ? "" : string.Join(" · ",
                totals.Select(t => $"{Capitalize(t.Type.Length > 0 ? t.Type : "damage")} {t.Total}")
                    .Append($"Total {totals.Sum(t => t.Total)}"));
        }
    }

    /// <summary>The d20 is resolved (or left blank) and all the damage is in.</summary>
    public bool IsComplete
    {
        get
        {
            var d20Blank = D20First.Trim().Length == 0 && (!HasSecondD20 || D20Second.Trim().Length == 0);
            if (!d20Blank && D20Used() is null) return false;
            if (D20Used() == 1) return true;
            return HasStructuredDamage ? _boxes.All(b => b.Value is not null) : FallbackValue is not null;
        }
    }

    public string LogLine
    {
        get
        {
            var used = D20Used();
            if (used == 1) return $"{Name}: natural 1 (miss)";
            var pieces = new List<string>();
            if (used is { } u) pieces.Add($"{u + AttackBonus} to hit{(IsCritical ? " (crit)" : "")}");
            if (HasStructuredDamage)
            {
                var totals = Totals();
                if (totals.Count > 0) pieces.Add(string.Join(" + ", totals.Select(t => $"{t.Total} {(t.Type.Length > 0 ? t.Type : "damage")}")));
            }
            else if (FallbackValue is { } f) pieces.Add($"{f} damage");
            var line = pieces.Count == 0 ? $"{Name}: no roll" : $"{Name}: {string.Join(", ", pieces)}";
            return IsComplete ? line : line + " (partial)";
        }
    }

    private int? D20Used() => AttackMath.D20Used(Mode, Parse20(D20First), Parse20(D20Second));

    private static int? Parse20(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 20 ? n : null;

    private static bool Invalid(string text) => text.Trim().Length > 0 && Parse20(text) is null;

    private IReadOnlyList<DamagePart> CurrentParts() =>
        _baseParts is null ? [] : IsCritical ? AttackMath.ForCritical(_baseParts) : _baseParts;

    private IReadOnlyList<(string Type, int Total)> Totals()
    {
        var sums = new int?[_baseParts?.Count ?? 0];
        for (var i = 0; i < _boxes.Count; i++) sums[_boxPartIndex[i]] = _boxes[i].Value;
        return AttackMath.Totals(CurrentParts(), sums);
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private void SetCritical(bool value)
    {
        if (!Set(ref _critical, value, nameof(IsCritical))) return;
        var parts = CurrentParts();
        for (var i = 0; i < _boxes.Count; i++) _boxes[i].Apply(parts[_boxPartIndex[i]]);
        Changed();
    }

    /// <summary>A d20 or damage entry changed: follow the d20 into or out of an automatic crit, then refresh the results.</summary>
    private void Changed()
    {
        var used = D20Used();
        if (used != _lastUsed)
        {
            _lastUsed = used;
            if (used == 20 && !_manualCrit)
            {
                _automaticCrit = true;
                SetCritical(true);
            }
            else if (used != 20 && _automaticCrit)
            {
                _automaticCrit = false;
                SetCritical(false);
            }
        }
        Raise(nameof(D20Error));
        Raise(nameof(ToHitText));
        Raise(nameof(TotalsText));
        Raise(nameof(IsComplete));
        Raise(nameof(LogLine));
    }

    /// <summary>Rolls the d20s first so a natural 20 doubles the dice before they are rolled.</summary>
    private void DoRollForMe()
    {
        if (Parse20(D20First) is null) D20First = _random.Next(1, 21).ToString(CultureInfo.InvariantCulture);
        if (HasSecondD20 && Parse20(D20Second) is null) D20Second = _random.Next(1, 21).ToString(CultureInfo.InvariantCulture);
        foreach (var box in _boxes.Where(b => b.Value is null))
            box.Input = AttackMath.RollDice(box.Part, _random).ToString(CultureInfo.InvariantCulture);
    }
}
