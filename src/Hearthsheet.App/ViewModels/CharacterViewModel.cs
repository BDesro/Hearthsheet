using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthsheet.App.Mvvm;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.App.ViewModels;

/// <summary>
/// The one view model for an open character, shared by the Sheet, Details and Play views. Stored values
/// bind straight to <see cref="Character"/>; derived values are computed here from CharacterRules and
/// refreshed whenever the session reports a change, so all views always show the same state.
/// </summary>
public sealed class CharacterViewModel : Observable, IDisposable
{
    private readonly CharacterSession _session;
    private readonly Dialogs _dialogs;
    private readonly ILogger _log;
    private bool _refreshQueued;

    public CharacterViewModel(CharacterSession session, RestService rest, Dialogs dialogs, ILogger log)
    {
        _session = session;
        _dialogs = dialogs;
        _log = log;
        var c = session.Character;

        Abilities = [.. AbilityInfo.All.Select(a => new AbilityRow(c, a))];
        Saves = [.. c.SavingThrows.Select(s => new SaveRow(c, s))];
        Skills = [.. c.Skills.Select(s => new SkillRow(c, s))];
        SpellLevels = [.. Enumerable.Range(0, 10).Select(l => new SpellLevelRow(l, l == 0 ? null : c.Spellcasting.SlotLevel(l)))];
        Conditions = [.. Enum.GetValues<Condition>().Select(x => new ConditionToggle(c, x))];

        CycleSkillProficiency = new RelayCommand<SkillRow>(row => row.Model.Proficiency = row.Model.Proficiency switch
        {
            ProficiencyLevel.None => ProficiencyLevel.Proficient,
            ProficiencyLevel.Proficient => ProficiencyLevel.Expertise,
            _ => ProficiencyLevel.None,
        });
        AddAttack = new RelayCommand(() => c.Attacks.Add(new Attack { Name = "New attack" }));
        AddItem = new RelayCommand(() => c.Items.Add(new Item { Name = "New item" }));
        AddWondrousItem = new RelayCommand(() => c.WondrousItems.Add(new Item { Name = "New wondrous item" }));
        AddWeapon = new RelayCommand(() =>
        {
            var weapon = new Weapon { Name = "New weapon" };
            c.Weapons.Add(weapon);
            c.Attacks.Add(new Attack { WeaponId = weapon.Id });
        });
        AddWeaponEffect = new RelayCommand(p => (p as Weapon)?.Effects.Add(new WeaponEffect { Name = "New effect" }));
        AddFeature = new RelayCommand(() => c.Features.Add(new Feature { Name = "New feature" }));
        AddProficiency = new RelayCommand(p => c.Proficiencies.Add(new Proficiency
        {
            Category = p is ProficiencyCategory cat ? cat : ProficiencyCategory.Other,
            // A weapon entry starts blank for picking types; a placeholder would show up as a custom type.
            Name = p is ProficiencyCategory.Weapon ? "" : "New proficiency",
        }));
        AddSpell = new RelayCommand(p => c.Spellcasting.Spells.Add(new Spell { Name = "New spell", Level = p is int l ? l : 0 }));
        AddResource = new RelayCommand(() => c.Resources.Add(new Resource { Name = "New resource" }));
        AddEffect = new RelayCommand(() => c.Effects.Add(new Effect { Name = "New effect" }));
        AddModifier = new RelayCommand(p => (p switch
        {
            Item i => i.Modifiers,
            Effect e => e.Modifiers,
            Feature f => f.Modifiers,
            _ => null,
        })?.Add(new Modifier { Target = StatTargets.ArmorClass, Value = 1 }));
        RemoveEntry = new RelayCommand(RemoveEntryWithConfirmation, p => p is not null);
        ApplyClassDefaults = new RelayCommand(() =>
        {
            if (SrdCatalog.FindClass(FirstClass.Name) is not { } def)
            {
                _dialogs.Info($"“{FirstClass.Name}” is not one of the built-in classes, so there are no defaults to apply.");
                return;
            }
            if (!_dialogs.Confirm($"Apply {def.Name} defaults for level {FirstClass.Level}? This sets hit die, saving throw proficiencies, " +
                                  "average maximum HP, spellcasting ability and spell slots.", "Apply class defaults"))
                return;
            CharacterFactory.ApplyClassDefaults(c, def);
            _log.LogInformation("Class defaults applied: {Class} {Level}", def.Name, FirstClass.Level);
        });

        Play = new PlayViewModel(this, rest, dialogs, log);
        _session.Changed += OnSessionChanged;
        SyncCollections();
    }

    public Character Character => _session.Character;
    public CharacterSession Session => _session;
    public PlayViewModel Play { get; }

    public IReadOnlyList<AbilityRow> Abilities { get; }
    public IReadOnlyList<SaveRow> Saves { get; }
    public IReadOnlyList<SkillRow> Skills { get; }
    public IReadOnlyList<SpellLevelRow> SpellLevels { get; }
    public IReadOnlyList<ConditionToggle> Conditions { get; }
    public ObservableCollection<AttackRow> Attacks { get; } = [];
    public ObservableCollection<WeaponRow> Weapons { get; } = [];

    // Derived values.
    public int ProficiencyBonus => CharacterRules.ProficiencyBonus(Character);
    public int ArmorClass => CharacterRules.ArmorClass(Character);
    public int Initiative => CharacterRules.Initiative(Character);
    public int Speed => CharacterRules.Speed(Character);
    public int MaxHitPoints => CharacterRules.MaxHitPoints(Character);
    public bool MaxHpModified => MaxHitPoints != Character.Combat.MaxHp;
    public int PassivePerception => CharacterRules.PassivePerception(Character);
    public int HitDiceTotal => CharacterRules.HitDiceTotal(Character);
    public string HitDiceText => string.Join(" · ", CharacterRules.HitDicePools(Character).Select(p => $"{p.Remaining} / {p.Total} d{p.Size}"));
    public string SpellSaveDc => CharacterRules.SpellSaveDc(Character)?.ToString() ?? "—";
    public string SpellAttackBonus => CharacterRules.SpellAttackBonus(Character) is { } b ? CharacterRules.FormatModifier(b) : "—";
    public string CarriedWeightText => $"{CharacterRules.CarriedWeight(Character):0.#} / {CharacterRules.CarryingCapacity(Character)} lb";
    public bool IsDying => CharacterRules.IsDying(Character);
    public bool IsDead => CharacterRules.IsDead(Character);
    public bool IsStable => Character.Combat.IsStable && Character.Combat.CurrentHp == 0 && !IsDead;
    public string VitalStatus => IsDead ? "Dead" : IsStable ? "Stable" : IsDying ? "Dying" : "";
    public string Title => string.IsNullOrWhiteSpace(Character.Identity.Name) ? "Unnamed character" : Character.Identity.Name;
    public string Summary => (Character.Identity.Classes.Count == 1
        ? $"{Character.Identity.Species} {CharacterRules.ClassSummary(Character)} {CharacterRules.TotalLevel(Character)}"
        : $"{Character.Identity.Species} {CharacterRules.ClassSummary(Character)}").Trim();
    /// <summary>The class entry the single-class editors bind to.</summary>
    public ClassLevel FirstClass => Character.Identity.Classes[0];
    public IReadOnlyList<string> WeaponTypeOptions => CharacterRules.WeaponTypeOptions(Character);
    public bool HasSpellcasting => Character.Identity.Classes.Any(e => e.SpellcastingAbility is not null) || Character.Spellcasting.Spells.Count > 0;

    public ICommand CycleSkillProficiency { get; }
    public ICommand AddAttack { get; }
    public ICommand AddItem { get; }
    public ICommand AddWondrousItem { get; }
    public ICommand AddWeapon { get; }
    public ICommand AddWeaponEffect { get; }
    public ICommand AddFeature { get; }
    public ICommand AddProficiency { get; }
    public ICommand AddSpell { get; }
    public ICommand AddResource { get; }
    public ICommand AddEffect { get; }
    public ICommand AddModifier { get; }
    public ICommand RemoveEntry { get; }
    public ICommand ApplyClassDefaults { get; }

    /// <summary>Coalesces bursts of model changes (e.g. a long rest touches dozens of values) into one refresh.</summary>
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.DataBind, Refresh);
    }

    private void Refresh()
    {
        _refreshQueued = false;
        SyncCollections();
        foreach (var row in Abilities) row.Refresh();
        foreach (var row in Saves) row.Refresh();
        foreach (var row in Skills) row.Refresh();
        foreach (var row in Attacks) row.Refresh();
        foreach (var row in Weapons) row.Refresh();
        foreach (var row in SpellLevels) row.Refresh();
        foreach (var row in Conditions) row.Refresh();
        Raise(string.Empty);
        Play.Refresh();
    }

    private void SyncCollections()
    {
        Sync(Attacks, Character.Attacks, r => r.Model, a => new AttackRow(Character, a));
        Sync(Weapons, Character.Weapons, r => r.Model, w => new WeaponRow(Character, w));
        foreach (var level in SpellLevels)
            Sync(level.Spells, Character.Spellcasting.Spells.Where(s => s.Level == level.Level).ToList(), s => s, s => s);
    }

    /// <summary>Makes <paramref name="rows"/> mirror <paramref name="models"/> by identity without rebuilding
    /// unchanged rows (rebuilding would steal keyboard focus from an editor in the list).</summary>
    private static void Sync<TModel, TRow>(ObservableCollection<TRow> rows, IList<TModel> models, Func<TRow, TModel> modelOf, Func<TModel, TRow> create)
        where TModel : class
    {
        for (var i = rows.Count - 1; i >= 0; i--)
            if (!models.Contains(modelOf(rows[i]))) rows.RemoveAt(i);
        for (var i = 0; i < models.Count; i++)
        {
            if (i < rows.Count && ReferenceEquals(modelOf(rows[i]), models[i])) continue;
            var existing = rows.Select((r, idx) => (r, idx)).FirstOrDefault(x => ReferenceEquals(modelOf(x.r), models[i]));
            if (existing.r is not null) rows.Move(existing.idx, i);
            else rows.Insert(i, create(models[i]));
        }
    }

    private void RemoveEntryWithConfirmation(object? entry)
    {
        var c = Character;
        (IList? list, string name) = entry switch
        {
            AttackRow r => (c.Attacks, r.Name),
            Attack a => (c.Attacks, a.Name),
            WeaponRow r => (c.Weapons, r.Model.Name),
            WeaponEffect e => (c.Weapons.Select(w => w.Effects).FirstOrDefault(list => list.Contains(e)), e.Name),
            Item i => (c.Items.Contains(i) ? c.Items : c.WondrousItems, i.Name),
            Feature f => (c.Features, f.Name),
            Proficiency p => (c.Proficiencies, p.Name),
            Spell s => (c.Spellcasting.Spells, s.Name),
            Resource r => (c.Resources, r.Name),
            Effect e => (c.Effects, e.Name),
            Modifier m => ((IList?)c.Items.Concat(c.WondrousItems).Select(i => i.Modifiers).Concat(c.Effects.Select(e => e.Modifiers))
                .Concat(c.Features.Select(f => f.Modifiers)).FirstOrDefault(list => list.Contains(m)), $"{m.Target} modifier"),
            _ => (null, ""),
        };
        if (list is null) return;
        var model = entry switch { AttackRow r => r.Model, WeaponRow r => r.Model, _ => entry };
        // Confirm only when there's something to lose; blank placeholder entries go without asking.
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("New ", StringComparison.Ordinal)
            && !_dialogs.Confirm($"Remove “{name}”?"))
            return;
        list.Remove(model);
        if (model is Weapon weapon)
            foreach (var attack in c.Attacks.Where(a => a.WeaponId == weapon.Id).ToList()) c.Attacks.Remove(attack);
        _log.LogDebug("Removed {Type}: {Name}", model!.GetType().Name, name);
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _session.Dispose();
    }
}
