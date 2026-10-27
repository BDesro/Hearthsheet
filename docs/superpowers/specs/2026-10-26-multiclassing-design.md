# Multiclassing: scope and requirements

Status: design approved through interview, not implemented. Target: a minor release (1.4.0) on a
`feature/multiclassing` branch off `dev`.

Decisions marked **(user)** came from the interview with Beau. Decisions marked **(builder's call)** were made while
writing this and can be changed without going back to the user.

## 1. Why

Beau's Echo Knight Fighter 11 takes a level of Blood Hunter at level 12. Hearthsheet stores one class, one subclass, one
level and one hit-die pool (`Identity.ClassName/Subclass/Level`, `Combat.HitDieSize/HitDiceRemaining`), so that
character can't be represented at all today. Beau is the only beta user.

**Success:** Beau clicks "Level up → Blood Hunter" on his level 11 Fighter. Hearthsheet then:

- warns that the multiclass prerequisites apply, without blocking;
- adds the Blood Hunter multiclass proficiencies;
- adds a d10 hit die;
- offers average or rolled HP;
- reports what it couldn't do (class features to add by hand).

Afterwards the character shows as Fighter 11 (Echo Knight) / Blood Hunter 1 in every view, with proficiency bonus +4 from
total level 12, and rests handle the 12d10 pool correctly.

## 2. Goals, non-goals, anti-goals

**Goals**

- A character holds 1..n class entries. Total level (the sum) drives proficiency bonus and hit dice.
- Built-in classes are the 12 SRD classes **plus Blood Hunter (user)**. A class the app doesn't know can still be
  typed in, with its hit die and spellcasting entered by hand.
- **Automate as much as possible, as long as it isn't too complex (user).** "Level up" handles everything mechanical.
  Rules are helpers, never gates: the table is **2014 rules with homebrew and mixed sources (user)**, so every
  automated value stays editable.
- Spellcasting **(user: "works for now")**:
  - spell slots come from the 2014 multiclass table (full + ½ half + ⅓ third casters);
  - Pact Magic is real, castable slots kept separate from the other slots;
  - each spellcasting class has its own ability, save DC and attack bonus;
  - spells are tagged with their class.
- Existing characters migrate losslessly to the new shape (schema 3).

**Non-goals (v1)**

- Adding class or subclass features automatically. No rules text may be bundled (`NOTICE.md`), so features stay manual.
  The level-up report reminds the player to add them.
- Subclass data: Echo Knight, Blood Hunter orders, and Order of the Profane Soul's own pact table. Profane Soul's slots
  are entered by hand in the Pact Magic row.
- 2024-rules variants (for example, half casters getting slots at level 1). Slot maximums stay hand-editable, as they
  are today.
- XP-driven levelling. XP stays a plain stored number with no thresholds or prompts **(builder's call)**.
- Undo or automatic reversal of a level-up. Lowering a level or removing a class row in Details is a plain edit; HP and
  slots are not recalculated **(builder's call)**.
- Tagging features or resources with a source class. Nothing would use it yet **(builder's call)**.

**Anti-goals** (it fails even if it technically works when…)

- Single-class players notice any change. The printed sheet of a single-class character must look exactly as it does
  today **(user, layout C)**.
- The app refuses or blocks something a homebrew table allows, such as unmet prerequisites, odd hit dice or a custom
  class.
- Derived values get stored (CLAUDE.md "stored vs derived").
- Old characters lose data on migration.

## 3. Constraints

- Follow CLAUDE.md and `docs/ARCHITECTURE.md`:
  - no new packages, no DI or MVVM toolkit;
  - Core stays free of WPF and I/O;
  - gameplay behaviour lives on the aggregate;
  - setters clamp;
  - every input has an `AutomationProperties.Name`;
  - add/remove controls on the sheet are hover-only;
  - the sheet stays 816×1056 and prints 1:1.
- Migrations go inline in `CharacterJson.Deserialize` on the raw `JsonObject` (`CharacterMigrator` was removed on
  purpose).
- **Schema 3 is fine to require everywhere (user).** Older apps will refuse schema-3 rows and files; "everyone updates".
- Blood Hunter: bundle **mechanical facts only** (name, hit die, saves, multiclass prerequisites and proficiencies,
  caster type), never rules text **(user)**. Add a `NOTICE.md` section that names it as Matt Mercer's / Critical Role's
  class, says the app includes only those bare facts, and makes no claim of affiliation.

## 4. Data model (schema 3)

### 4.1 Class entries

Add `Identity.Classes : ObservableCollection<ClassLevel>` and remove `Identity.ClassName`, `Identity.Subclass` and
`Identity.Level`.

```text
ClassLevel : Observable
  Name                 string      "Fighter", "Blood Hunter", "Homebrew Witch"
  Subclass             string      "Echo Knight"
  Level                int 1..20
  HitDie               int 4..20   (same clamp as today's HitDieSize)
  HitDiceRemaining     int 0..20   Normalize clamps it to ≤ Level
  Caster               CasterType  None | Full | Half | Third | Pact   (move the enum from Content to Domain/Enums.cs)
  SpellcastingAbility  Ability?
```

- **The first entry is the starting class.** It sets save proficiencies, max die at level 1 and full (not multiclass)
  proficiencies. Rows can be reordered in Details.
- Hit dice are stored per class entry. A "pool" is a derived grouping by die size: two d10 classes share one d10 pool.
  Spending a d10 decrements the first entry of that size that has dice left **(builder's call: simpler than a separate
  pool collection that must be reconciled with the class list)**.
- Remove `Combat.HitDieSize`, `Combat.HitDiceRemaining` and `Spellcasting.Ability`.
- Keep `Spellcasting.SpellcastingClass` as an optional free-text label **(builder's call)**. When there is one caster,
  the sheet's "Spellcasting Class" box still binds to it two-way, so a single-class sheet is unchanged and "Wizard
  (Evocation)" survives. When it's blank, the box shows the primary caster's name as a placeholder. With several
  casters, the label is ignored and the box shows per-class lines (§8).
- `Normalize()`:
  - guarantees at least one entry (blank name, level 1);
  - clamps each `HitDiceRemaining` to ≤ its `Level`;
  - if the total level exceeds 20, lowers the last entries until it is 20.

  This replaces the current `HitDiceRemaining > Identity.Level` line.

### 4.2 Spellcasting

- `Spell.Class : string`. A blank value means the primary caster: the first entry with a `SpellcastingAbility`.
- `Spellcasting.PactSlots : PactSlots { Level 1..5, Maximum 0..20, Expended 0..20, Available (computed) }`. Use the same
  pattern as `SpellSlotLevel`; reusing `SpellSlotLevel` itself is fine if its `Level` clamp fits.
- `Spellcasting.Slots` (levels 1..9, stored maximums) is unchanged. Maximums stay stored because the user can edit them;
  level-up and "Apply class defaults" recompute them.

### 4.3 Migration 2 → 3 (in `CharacterJson.Deserialize`, before typed deserialization)

`identity.classes = [ {...} ]` with:

| New field | Source |
|---|---|
| `name` | `identity.className` |
| `subclass` | `identity.subclass` |
| `level` | `identity.level` |
| `hitDie` | `combat.hitDieSize` |
| `hitDiceRemaining` | `combat.hitDiceRemaining` |
| `spellcastingAbility` | `spellcasting.ability` |
| `caster` | `SrdCatalog.FindClass(name)?.Caster`, else `Full` if `spellcasting.ability` is set, else `None` |

Then remove the old keys.

- `spellcasting.spellcastingClass` stays as it is (see §4.1).
- The Pact resource that `ApplyClassDefaults` created (name starts with `Pact Magic slots (level N)`) becomes
  `spellcasting.pactSlots` with `level = N`, `maximum = resource.maximum` and `expended = maximum − current`. The
  resource is then removed. Resources with any other name are left alone.
- Missing keys fall back to the class defaults (documents from older versions may lack some fields).
- Bump `CurrentSchemaVersion` to 3 and update its doc comment and `ARCHITECTURE.md` §3, §7 and §14.

## 5. Content (`Core/Content/SrdCatalog.cs`)

Extend `ClassDefinition` with:

- `MulticlassPrerequisites`: a list of alternatives, each a set of ability minimums. For example, Fighter is
  `[{Str 13}, {Dex 13}]` and Monk is `[{Dex 13, Wis 13}]`.
- `MulticlassProficiencies`: a list of `(ProficiencyCategory, Name)`.
- `MulticlassSkillChoices`: the number of skills to pick (Bard 1, Ranger 1, Rogue 1).

SRD 5.1 multiclassing facts (verify against SRD 5.1 while building):

| Class | Prerequisite | Proficiencies gained |
|---|---|---|
| Barbarian | Str 13 | Shields, simple weapons, martial weapons |
| Bard | Cha 13 | Light armor, one skill, one musical instrument |
| Cleric | Wis 13 | Light armor, medium armor, shields |
| Druid | Wis 13 | Light armor, medium armor, shields |
| Fighter | Str 13 or Dex 13 | Light armor, medium armor, shields, simple weapons, martial weapons |
| Monk | Dex 13 and Wis 13 | Simple weapons, shortswords |
| Paladin | Str 13 and Cha 13 | Light armor, medium armor, shields, simple weapons, martial weapons |
| Ranger | Dex 13 and Wis 13 | Light armor, medium armor, shields, simple weapons, martial weapons, one class skill |
| Rogue | Dex 13 | Light armor, one class skill, thieves' tools |
| Sorcerer | Cha 13 | — |
| Warlock | Cha 13 | Light armor, simple weapons |
| Wizard | Int 13 | — |

**Blood Hunter** (2020 revision):

- Hit die d10.
- Saves: Dexterity and Intelligence.
- Caster type: None.
- Multiclass prerequisite: Int 13 and (Str 13 or Dex 13).
- Multiclass proficiencies: light armor, medium armor, shields, simple weapons, martial weapons, alchemist's supplies.

> Prerequisite confirmed on dnd5e.wikidot.com/blood-hunter (cites D&D Beyond): "an Intelligence score of 13 or higher
> and a Strength or Dexterity score of 13 or higher". The saves and hit die match the same page. The multiclass
> proficiencies come from D&D Beyond listings (wikidot doesn't list them separately). Profane Soul's spellcasting is a
> subclass feature and is out of scope (see §2).

The "one instrument" and "one skill" entries can't be picked automatically. They go in the level-up report under *Needs
attention*.

**Third casters:** Fighter and Rogue have caster type None. When an entry's subclass is set to "Eldritch Knight" or
"Arcane Trickster" (case-insensitive) and its caster type is None, set it to Third with ability INT **(builder's call:
these names appear in SRD 5.1's multiclassing rules; nothing else is bundled)**. The user can change it back.

## 6. Rules (`Core/Rules/CharacterRules.cs`), all derived

- `TotalLevel(c)` = sum of entry levels (1..20). `ProficiencyBonus(c)` uses it.
- `HitDiceTotal(c)` = `TotalLevel`. Add `HitDicePools(c)` returning `(Size, Remaining, Total)` per die size, largest
  first.
- `SpellSaveDc(c, ClassLevel)` and `SpellAttackBonus(c, ClassLevel)` replace the single-ability versions; a convenience
  overload covers the primary caster. A spell's DC comes from its `Class`, or from the primary caster when `Class` is
  blank. The `spell.dc` and `spell.attack` modifiers apply to every class.
- Spell slots (in `SrdCatalog`, which already owns the table):
  - **One class with Full/Half/Third casting:** that class's own progression. Full = level. Half = ⌈level/2⌉ from
    level 2 (current behaviour). Third = ⌈level/3⌉ from level 3.
  - **Two or more:** multiclass caster level = Σ Full + Σ ⌊Half/2⌋ + Σ ⌊Third/3⌋, looked up in `FullCasterSlots`.
  - Pact casters don't count toward that total. `PactSlots(warlockLevel)` sets `Spellcasting.PactSlots`.
- **Recompute only what has a source (builder's call).** Regular slot maximums are recomputed only when at least one
  entry is Full, Half or Third. Pact slots are recomputed only when at least one entry is Pact. Otherwise each is left
  exactly as stored, so Profane Soul pact slots or homebrew slots typed in by hand survive every level-up and "Apply
  class defaults". This also stops a non-caster level-up from zeroing hand-entered slots.

## 7. Behaviour

### 7.1 Level up (new; `CharacterFactory.LevelUp`, in Content next to `ApplyClassDefaults`)

`LevelUp(Character c, string className, LevelUpOptions o) → LevelUpReport`. The report has `Changes` and
`NeedsAttention`, like `RestReport`. Steps:

1. Refuse only when the total level is already 20.
2. **Existing class:** `Level++`. **New class:** append an entry. Fill it from `SrdCatalog.FindClass`, or from
   `o.HitDie`, `o.Caster` and `o.Ability` for a custom class.
3. **New class, not the first:**
   - check prerequisites for the new class **and** every existing class, and report unmet ones as warnings;
   - add multiclass proficiencies, skipping any the character already has (case-insensitive name within the same
     category);
   - put skill and instrument choices under *Needs attention*.

   Saving throws are not changed.
4. HP: `Combat.MaxHp += max(1, gain + CON mod)`. The gain is either `HitDie/2 + 1` (average) or the roll from
   `o.Roll`. **Both are offered every time (user).** Current HP rises by the same amount.
5. The entry's `HitDiceRemaining++`.
6. Recompute spell slot maximums and pact slots (§6). `Expended` values are kept, clamped to the new maximums.
7. *Needs attention*: "Add <Class> <level> features by hand."

App: a "Level up…" dialog opens from Details and from Play.

- **Class picker:** existing classes, built-in classes, or a typed name. A custom name reveals hit die, caster type and
  ability fields.
- **HP:** "Take average (n)" or "Roll": an in-app roll, or a physical roll typed in, the same pattern as
  `ShortRestDialog`.
- **Warnings** are shown inline and don't block.
- **Result:** the report is shown as the result and logged.

A 1st-level character's first entry is still created by `NewCharacterDialog`/`CharacterFactory.Create`, as today.

### 7.2 Apply class defaults (multi-entry)

This recomputes all automated values from the entries, with the confirmation it asks for today. It uses each
entry's **stored** hit die, caster type and ability. The catalog fills these only when a built-in entry's value is
blank, so an Eldritch Knight's Third or a homebrew hit die is never undone **(builder's call)**. Custom classes are no
longer refused; their stored values are used as they are.

- saves from the first entry only;
- max HP = first entry's max die + the average of every other level, each plus CON;
- hit dice per entry reset to full (today it doesn't touch them; factory `Create` sets them);
- spellcasting abilities from the catalog where blank;
- slots and pact slots per §6.

It does not touch proficiencies. Those are only added by level-up **(builder's call: re-adding would duplicate the
user's edits)**.

### 7.3 Hit dice and rests

- `SpendHitDie(int size, int roll)`: validates the roll against that size and spends from that pool. The existing
  `SpendHitDie(int roll)` either becomes the single-pool overload or is removed.
- Short rest dialog: when there is more than one pool, a die-size picker ("d10 (9 left)", "d8 (1 left)"). With one pool
  it looks as it does today.
- `LongRestHitDice`: regain `max(1, TotalLevel/2)` dice, **filled from the largest dice first** **(builder's call;
  2014 lets the player choose, but picking adds a dialog to every long rest)**. The report says which, e.g.
  "Regained 5d10, 1d8".
- `SpellSlotRecovery`: pact slots recover on **short and long** rests, regular slots on long rests only.
- `CastSpell`: picks the lowest available slot at or above the spell level from either the regular slots or the pact
  slots (pact slots only when `PactSlots.Level ≥` the spell level). On a tie, use the pact slot, since it comes back on
  a short rest **(builder's call)**. There is no slot-choice parameter. Pact pips behave like regular pips: clicking one
  toggles expended through new `ExpendPactSlot`/`RestorePactSlot` methods. That's how a player spends a pact slot
  instead of the one the Cast button chose.

## 8. Views

Sheet, Details and Play still bind to the same aggregate; there's no per-view state.

**Sheet (layout C, user):**

- **One class:** header boxes unchanged. Class, Level and Subclass bind two-way to `Classes[0]`.
- **Two or more classes:** Class, Level and Subclass are replaced by one two-cell-wide calculated (shaded) box,
  "Class & Level · total N", showing `Fighter 11 (Echo Knight) / Blood Hunter 1`. It's edited in Details, not on the
  sheet. The freed cell keeps the remaining fields in the same 4×2 grid.
- **Hit dice box:** one pool shows "Total 12d10" with remaining "12" as today, still editable. Several pools show
  "Total 11d10 + 1d8" with remaining "9d10 · 1d8", calculated and edited in Details.
- **Spell page header:** one caster, unchanged (ability combo bound to the primary caster's entry). Several casters show
  one compact line per class in the Spellcasting Class / Ability / DC / Attack strip (e.g. "Wizard · INT · DC 15 · +7").
  An extra Pact Magic row of pips appears only when `PactSlots.Maximum > 0`.
- Everything must still fit 816×1056 and print 1:1. Add/remove controls stay hover-only.

**Details:**

- A **Classes** table replaces the Class, Subclass and Level fields, with columns class (editable combo of built-ins),
  subclass, level, hit die, hit dice left, caster type and spellcasting ability.
- Hover add/remove and reorder (the first row is marked "starting class").
- "Level up…" and "Apply class defaults…" buttons.
- The proficiency bonus line shows the total level.
- The spells table gains a Class column only when there are two or more casting entries.
- The Pact Magic level, maximum and expended fields are editable (this is where Profane Soul is entered).

**Play:**

- The header summary reads "Fighter 11 / Blood Hunter 1".
- A "Level up…" button.
- The short-rest die picker (§7.3).
- Pact pips next to the slot pips.
- The spell DC shown is per spell's class.

**Elsewhere:**

- `CharacterViewModel.Summary` and the SQLite `description` column (`SqliteCharacterRepository` line ~331) use
  `Species Fighter 11 / Blood Hunter 1`.
- The `CharacterLibrary` log line lists the classes.
- `Lookups.ClassNames` includes Blood Hunter.

## 9. Persistence, import/export, sync

- SQLite: no table change. `schema_version` = 3 on save, and `meta.db_version` is unchanged.
- `.dndchar`: the envelope `schemaVersion` becomes 3. Importing a v2 file migrates it, and a v4 file is refused, as
  today. Add `ClassLevel` entries to `EnforceLimits` (cap the class count, e.g. ≤ 20).
- Cloud sync: no server change. An older app skips schema-3 rows ("Update Hearthsheet to sync …") and holds its pull
  cursor; this is accepted (§3). A schema-2 row pulled by the new app migrates through the same pipeline.

## 10. Tests (`tests/Hearthsheet.Tests`, existing classes)

- **Migration** (`PersistenceTests`):
  - a v2 document with class, level, hit dice, spellcasting ability and a Pact resource migrates to one entry plus
    pact slots;
  - a differing `spellcastingClass` ends up in notes;
  - a v2 `.dndchar` imports;
  - v3 round-trips;
  - v4 is refused.
- **Rules** (`RulesTests`):
  - proficiency bonus from total level (Fighter 11 / Blood Hunter 1 → +4);
  - slots: single half caster (Paladin 5 → 4/2), single third caster (EK 7 → 4/2), Paladin 2 / Sorcerer 3 → caster
    level 4, Warlock 3 / Sorcerer 2 → pact 2×2nd plus regular slots from Sorcerer 2;
  - per-class DC;
  - Eldritch Knight auto-Third.
- **Level up** (new tests in `RulesTests` or `GameplayTests`):
  - prerequisite warnings, both directions;
  - proficiency dedupe;
  - skill choice reported;
  - average vs roll HP;
  - total level 20 refused;
  - custom class.
- **Rests** (`RestTests`):
  - spending by die size;
  - long rest largest-first across pools;
  - pact slots on a short rest.
- **Gameplay:** `CastSpell` with pact slots.
- **Normalize:** no entries; remaining dice above level; total above 20.
- **Sync** (`CloudSyncTests`): a schema-2 cloud row reads and migrates.

## 11. Docs and release

- Update `ARCHITECTURE.md`: §3 domain tree, §5 rests, §7 schema 3 and its migration, §13 test count, §14 (remove the
  single-class limitation; note Profane Soul and features are manual).
- Update `NOTICE.md`: the Blood Hunter section (§3).
- Release as 1.4.0, with `RELEASE_NOTES.md` as plain `-` bullets, through the normal dev → main PR, only when asked.

## 12. Left to the builder

- Exact dialog layout and wording, and how the Details Classes table is styled.
- Whether `PactSlots` reuses `SpellSlotLevel` or is its own small class.
- How the sheet switches between the one-class and multi-class header (a `DataTrigger` on `Classes.Count`, two
  templates, or similar).
- Splitting the work into commits or PRs.
