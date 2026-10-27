using System.Collections.Specialized;
using System.ComponentModel;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;

namespace Hearthsheet.Core.Application;

/// <summary>
/// The open character: tracks every change to the model (whichever view made it) and whether it
/// has been saved. Views subscribe to <see cref="Changed"/> to refresh derived values.
/// It also keeps class edits consistent: a caster type change recomputes spell slots, and renaming a class
/// retags the spells cast as it.
/// </summary>
public sealed class CharacterSession : IDisposable
{
    private readonly ChangeTracker _tracker;
    private int _changeVersion;
    private int _savedVersion;
    /// <summary>Each class entry's last non-blank name, so a rename knows which spells to retag.</summary>
    private readonly Dictionary<ClassLevel, string> _classNames = [];

    public CharacterSession(Character character)
    {
        Character = character;
        _tracker = new ChangeTracker(character, OnChanged);
        character.Identity.Classes.CollectionChanged += OnClassesChanged;
        foreach (var entry in character.Identity.Classes) Watch(entry);
    }

    public Character Character { get; }
    public bool IsDirty => _changeVersion != _savedVersion;

    public event EventHandler? Changed;

    /// <summary>Records that the current state is persisted.</summary>
    public void MarkSaved() => _savedVersion = _changeVersion;

    private void OnChanged()
    {
        _changeVersion++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnClassesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (ClassLevel entry in e.OldItems) Unwatch(entry);
        if (e.NewItems is not null) foreach (ClassLevel entry in e.NewItems) Watch(entry);
    }

    private void Watch(ClassLevel entry)
    {
        _classNames[entry] = entry.Name.Trim();
        entry.PropertyChanged += OnClassChanged;
    }

    private void Unwatch(ClassLevel entry)
    {
        _classNames.Remove(entry);
        entry.PropertyChanged -= OnClassChanged;
    }

    private void OnClassChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ClassLevel entry) return;
        if (e.PropertyName == nameof(ClassLevel.Caster)) CharacterFactory.RecomputeSlots(Character);
        if (e.PropertyName != nameof(ClassLevel.Name)) return;

        // A blank name is mid-edit: keep the old one so the next keystroke still finds its spells.
        var name = entry.Name.Trim();
        if (name.Length == 0) return;
        var old = _classNames[entry];
        _classNames[entry] = name;
        if (old.Length == 0) return;
        foreach (var spell in Character.Spellcasting.Spells)
            if (string.Equals(spell.Class.Trim(), old, StringComparison.OrdinalIgnoreCase)) spell.Class = name;
        if (string.Equals(Character.Spellcasting.SpellcastingClass.Trim(), old, StringComparison.OrdinalIgnoreCase))
            Character.Spellcasting.SpellcastingClass = name;
    }

    public void Dispose()
    {
        Character.Identity.Classes.CollectionChanged -= OnClassesChanged;
        foreach (var entry in _classNames.Keys.ToList()) Unwatch(entry);
        _tracker.Dispose();
    }
}
