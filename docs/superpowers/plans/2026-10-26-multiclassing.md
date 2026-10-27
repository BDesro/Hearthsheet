# Multiclassing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a character hold several class entries (e.g. Fighter 11 (Echo Knight) / Blood Hunter 1), with a
level-up flow, multiclass spell slots, Pact Magic slots, per-class spellcasting and per-die-size hit-dice pools.

**Architecture:**

- `Identity.Classes` (a list of `ClassLevel`) replaces the single class, level and hit-die fields.
- Stored data is migrated 2 → 3 on the raw `JsonObject` in `CharacterJson.Deserialize`.
- Everything derived (total level, pools, slots, DCs) is a pure function in `CharacterRules`/`SrdCatalog`.
- Level-up lives next to `ApplyClassDefaults` in `CharacterFactory`. Views bind to the same aggregate.

**Tech Stack:** .NET 10, WPF, System.Text.Json, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-10-26-multiclassing-design.md`. Read it with this plan; section numbers like
"spec §7.1" refer to it.

## Global Constraints

- **Code conventions:**
  - No new NuGet packages, DI container or MVVM toolkit.
  - Core has no WPF or I/O references; tests reference Core, Infrastructure and Updater, never App.
  - Never store a derived value. Property setters clamp. Gameplay behaviour lives on the aggregate.
- **Migrations:** inline in `CharacterJson.Deserialize`, on the raw `JsonObject`, before typed deserialization. No
  migrator class.
- **UI rules:**
  - Every WPF input has an `AutomationProperties.Name`, and state is never shown by colour alone.
  - The sheet stays 816×1056 DIP and prints 1:1. Add/remove controls are hover-only.
  - A single-class character's printed sheet must look exactly as it does today.
- **Content:** bundle Blood Hunter as mechanical facts only, with no rules text.
- **Rules:** warn, never block, except at total level 20.
- **Git:**
  - Branch `feature/multiclassing` off `dev`; open the PR into `dev` only.
  - Never push or PR to `main`. Don't bump `<Version>` or edit `RELEASE_NOTES.md`; that happens at release, when asked.
  - Each task ends green: `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx` both pass.

## Review Focus

These inputs aren't covered by the spec's happy paths. Each line has a test in the task named.

1. **A v1/v2 document with no `identity` or `combat` object at all** (e.g. `{}`) must load as one blank entry at
   level 1 with 1 hit die, not crash. Covered in Task 1.
2. **An imported file whose class levels sum above 20, or that has zero classes,** must be repaired by `Normalize()`:
   trimmed from the last entry, or given one blank entry. Covered in Task 1.
3. **Spending a die size that's empty while another pool still has dice** must throw and leave every pool unchanged; it
   must not spend from the other pool. Covered in Task 1.
4. **Levelling up a non-caster** (Blood Hunter with Caster None) must leave hand-typed regular and pact slots exactly
   as they were. Covered in Task 4.
5. **Removing the last class row in Details** must be impossible: the remove control is hidden when
   `Classes.Count == 1`, and `Normalize` re-adds an entry if one ever goes missing. Covered in Tasks 1 and 5.

---

### Task 0: Branch setup

- [ ] **Step 1:** Create a worktree on a new branch `feature/multiclassing` from `origin/dev`, using
  superpowers:using-git-worktrees. The main checkout has unrelated uncommitted work, so don't switch branches there.
- [ ] **Step 2:** Copy the spec and this plan from the main checkout into the worktree's `docs/superpowers/specs/` and
  `docs/superpowers/plans/`.
- [ ] **Step 3:** Run `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: both pass.
- [ ] **Step 4:** Commit with
  `git add docs/superpowers && git commit -m "docs: multiclassing spec and plan"`.

---

### Task 1: Class entries, schema 3 migration, hit-dice pools

The model change breaks every call site at once, so the App is rebound in this same task (binding to the first class
entry) so that the solution still builds. Single-class behaviour and looks don't change.

**Files:**

- Modify:
  - `src/Hearthsheet.Core/Domain/CharacterParts.cs` (`Identity`, `Combat`, new `ClassLevel`)
  - `src/Hearthsheet.Core/Domain/Enums.cs` (move `CasterType` here and add `Third`)
  - `src/Hearthsheet.Core/Domain/Character.cs` (`Normalize`)
  - `src/Hearthsheet.Core/Domain/Character.Play.cs` (`SpendHitDie`)
  - `src/Hearthsheet.Core/Rules/CharacterRules.cs`
  - `src/Hearthsheet.Core/Rules/Resting.cs` (`LongRestHitDice`)
  - `src/Hearthsheet.Core/Serialization/CharacterJson.cs`
  - `src/Hearthsheet.Core/Serialization/PortableCharacterFile.cs` (`EnforceLimits`)
  - `src/Hearthsheet.Core/Content/SrdCatalog.cs` (remove the enum; adapt `Create`/`ApplyClassDefaults` to `Classes[0]`)
  - `src/Hearthsheet.Core/Application/CharacterLibrary.cs:28`
  - `src/Hearthsheet.Infrastructure/Persistence/SqliteCharacterRepository.cs:~331`
  - App: `CharacterViewModel.cs`, `PlayViewModel.cs`, `ShortRestDialog.xaml(.cs)`, `DetailsView.xaml`, `SheetView.xaml`
  - `docs/ARCHITECTURE.md` §3 and §7
- Test:
  - `tests/Hearthsheet.Tests/PersistenceTests.cs`
  - `RulesTests.cs`, `GameplayTests.cs`, `RestTests.cs`
  - fix the existing tests that set `Identity.Level` / `Combat.HitDieSize`

**Interfaces:**

- Produces:
  - `public enum CasterType { None, Full, Half, Third, Pact }` in `Hearthsheet.Core.Domain`.
  - `public sealed class ClassLevel : Observable` with these properties, in this declaration order (the order matters;
    see Step 5): `Name` (string), `Subclass` (string), `Level` (int, clamp 1..20, default 1), `HitDie` (int, clamp
    4..20, default 8), `HitDiceRemaining` (int, clamp 0..20, default 1), `Caster` (CasterType),
    `SpellcastingAbility` (Ability?).
  - `Identity.Classes : ObservableCollection<ClassLevel>`, initialized `[new ClassLevel()]`. Remove
    `Identity.ClassName`, `Subclass` and `Level`. Remove `Combat.HitDieSize` and `HitDiceRemaining`.
  - `CharacterRules.TotalLevel(Character) : int`. `ProficiencyBonus(Character)` uses it. `HitDiceTotal(c) == TotalLevel(c)`.
  - `public sealed record HitDicePool(int Size, int Remaining, int Total)` and
    `CharacterRules.HitDicePools(Character) : IReadOnlyList<HitDicePool>`, grouped by `HitDie`, largest first.
  - `CharacterRules.ClassSummary(Character) : string`. One entry gives `"Fighter"`; several give
    `"Fighter 11 (Echo Knight) / Blood Hunter 1"`. Subclass is in parentheses only when non-blank.
  - `Character.SpendHitDie(int size, int roll) : int`. Remove the one-argument overload.
  - `CharacterJson.CurrentSchemaVersion = 3`.

- [ ] **Step 1: Write failing migration and repair tests (PersistenceTests)**

```csharp
[Fact] public void SchemaTwo_MigratesSingleClassToOneEntry()
{
    const string v2 = """{"identity":{"className":"Fighter","subclass":"Echo Knight","level":11},
        "combat":{"hitDieSize":10,"hitDiceRemaining":7},"spellcasting":{"ability":null}}""";
    var c = CharacterJson.Deserialize(v2, 2);
    var e = Assert.Single(c.Identity.Classes);
    Assert.Equal(("Fighter", "Echo Knight", 11, 10, 7, CasterType.None), (e.Name, e.Subclass, e.Level, e.HitDie, e.HitDiceRemaining, e.Caster));
}
[Fact] public void SchemaTwo_UnknownClassWithAbility_BecomesFullCaster()   // className "Witch", spellcasting.ability "Intelligence" → Caster Full, SpellcastingAbility Intelligence
[Fact] public void SchemaTwo_EmptyDocument_LoadsOneBlankLevelOneEntry()   // Deserialize("{}", 2) and ("{}", 1): one entry, Name "", Level 1, HitDiceRemaining 1
[Fact] public void SchemaThree_RoundTripsClasses()                         // two entries survive Serialize/Deserialize(…, 3)
[Fact] public void Normalize_RepairsClasses()
{
    // An imported file: classes [] → one entry. Classes [Fighter 15, Wizard 10] → Wizard trimmed to 5 (total 20).
    // An entry with level 3 and hitDiceRemaining 9 → 3.
}
```

  Also update `Import_RejectsNewerSchema` and `Load_RowFromNewerSchema_ThrowsFormatException`; they already use
  `CurrentSchemaVersion`, so they only need to compile.

- [ ] **Step 2: Write failing rules, gameplay and rest tests**

  - RulesTests:
    - `ProficiencyBonus_UsesTotalLevel`: entries Fighter 11 + Blood Hunter 1 → `TotalLevel` 12, PB +4.
    - `HitDicePools_GroupByDieSize`: Fighter 11 d10 (9 left), Rogue 1 d8 (1 left), Paladin 2 d10 (2 left) →
      `[(10, 11, 13), (8, 1, 1)]`.
    - `ClassSummary_SingleAndMulti`: the two strings given under Interfaces.
  - GameplayTests:
    - Rewrite the `Fighter()` helper as one entry with `HitDie 10`, `Level 4`, `HitDiceRemaining 4`.
    - `SpendHitDie_HealsRollPlusCon_AndValidatesRoll` uses `SpendHitDie(10, 6)`; a roll of 11 throws.
    - New `SpendHitDie_EmptyPoolThrows_OtherPoolUntouched`: d10 entry with 0 left and d8 entry with 1 left.
      `SpendHitDie(10, 5)` throws `InvalidOperationException`, and the d8 entry still has 1.
    - New `SpendHitDie_UnknownSizeThrows`: `SpendHitDie(12, 5)` on a character with no d12 throws.
  - RestTests:
    - `LongRest_RegainsHalfTotal_LargestDiceFirst`: d10 × 11 (0 left) plus d8 × 1 (0 left), long rest → regains 6.
      The d10 entry has 6, the d8 entry 0, and the report contains `"Regained 6d10"`.
    - `LongRest_FillsNextPoolWhenLargestFull`: d10 × 2 (1 left) plus d8 × 4 (0 left), total 6, regain 3 → d10 2,
      d8 2, report `"Regained 1d10, 2d8"`.

- [ ] **Step 3: Run the tests to confirm they fail**

  Run: `dotnet test Hearthsheet.slnx`. Expected: compile errors or failures in the new tests.

- [ ] **Step 4: Implement the model, rules, `SpendHitDie(size, roll)` and `LongRestHitDice`**

  - `SpendHitDie`: validate the roll against `size`, then decrement the first entry with `HitDie == size &&
    HitDiceRemaining > 0`. If no entry has that size or none has dice left, throw.
  - `LongRestHitDice`: regain `max(1, TotalLevel / 2)`, going through entries ordered by `HitDie` descending and
    filling each up to its `Level`. Write one change line in the form `"Regained 1d10, 2d8 (n/total)."`.
  - `Normalize`:
    - add `new ClassLevel()` if `Classes` is empty;
    - clamp each `HitDiceRemaining` to its `Level`;
    - while `TotalLevel > 20`, lower the last entry (removing it at level 1, but never the only entry).

    Delete the old `HitDiceRemaining > Identity.Level` line.
  - `EnforceLimits`: add `c.Identity.Classes.Count` to the counts and reject more than 20 entries (use a separate check,
    not the 5000 cap).

- [ ] **Step 5: Implement the 2 → 3 migration in `CharacterJson.Deserialize(JsonObject, int)`**

  When `schemaVersion < 3`, before `node.Deserialize`:
  - get or create `identity`, `combat` and `spellcasting`;
  - build `identity.classes = [{ name, subclass, level, hitDie, hitDiceRemaining, caster, spellcastingAbility }]` from
    `identity.className`, `identity.subclass`, `identity.level`, `combat.hitDieSize`, `combat.hitDiceRemaining` and
    `spellcasting.ability`;
  - fall back to the old C# defaults for any missing key: level 1, die 8, remaining 1;
  - set `caster` = `SrdCatalog.FindClass(name)?.Caster` as a string, else `"Full"` if the ability is set, else `"None"`;
  - remove the five old keys plus `spellcasting.ability`;
  - keep `spellcasting.spellcastingClass`.

  Update the `CurrentSchemaVersion` doc comment to describe version 3. Declaration order matters because Task 3's
  `Subclass` setter can change `Caster`: JSON properties deserialize in declaration order, so `Caster` must come after
  `Subclass` to win.

- [ ] **Step 6: Rebind the App so it builds**

  Single-class looks don't change.
  - `CharacterViewModel`:
    - `Summary` → `$"{Species} {ClassSummary} {TotalLevel}"` for one entry (unchanged text), or
      `$"{Species} {ClassSummary}"` for several;
    - `HitDiceText` → pools joined, e.g. `"9 / 11 d10 · 1 / 1 d8"`;
    - `HasSpellcasting` → any entry has `SpellcastingAbility` or there are spells;
    - add `public ClassLevel FirstClass => Character.Identity.Classes[0]`.
  - Repository description and `CharacterLibrary` log: use `ClassSummary` plus the total level, the same way as
    `Summary`.
  - Details, Sheet and NewCharacter bindings: `Identity.ClassName/Subclass/Level` → `FirstClass.Name/Subclass/Level`,
    and `Combat.HitDieSize/HitDiceRemaining` → `FirstClass.HitDie/HitDiceRemaining`.
  - Spellcasting ability combos → `FirstClass.SpellcastingAbility`.
  - Short rest dialog: use `HitDicePools(c)[0].Size` for now (Task 5 adds the picker), and call
    `PlayViewModel.SpendHitDie(int size, int roll)`.
  - `CharacterRules.SpellSaveDc`/`SpellAttackBonus` read the ability from the first entry with a `SpellcastingAbility`.
    Task 2 replaces this.

- [ ] **Step 7: Run the tests to confirm they pass**

  Run: `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: everything passes.

- [ ] **Step 8: Update the docs**

  `ARCHITECTURE.md`:
  - §3: Identity now holds `Classes[]`, and Combat loses the hit-die fields.
  - §7: schema 3 and what its migration does.

- [ ] **Step 9: Commit**

  `git commit -am "feat: class entries, schema 3 migration and hit-dice pools"`. Add the new files explicitly if there
  are any.

---

### Task 2: Per-class spellcasting, multiclass slots, Pact Magic slots

**Files:**

- Modify:
  - `CharacterParts.cs` (`Spell.Class`, new `PactSlots`, `Spellcasting.PactSlots`)
  - `SrdCatalog.cs` (slot calculation)
  - `CharacterRules.cs` (DC and attack per class)
  - `Character.Play.cs` (`CastSpell`, pact expend/restore)
  - `Resting.cs` (`SpellSlotRecovery`)
  - `CharacterJson.cs` (the pact-resource migration, added to the same `< 3` block)
  - `PlayViewModel.cs` / `PlayView.xaml` (pact pips)
  - `docs/ARCHITECTURE.md` §5
- Test: `RulesTests.cs`, `GameplayTests.cs`, `RestTests.cs`, `PersistenceTests.cs`

**Interfaces:**

- Consumes: `ClassLevel`, `CasterType`, `TotalLevel` (Task 1).
- Produces:
  - `Spell.Class : string` (blank = primary caster).
  - `public sealed class PactSlots : Observable` with `Level` (clamp 1..5, default 1), `Maximum` (0..20),
    `Expended` (0..20) and computed `Available = max(0, Maximum − Expended)`, raised like `SpellSlotLevel`.
  - `Spellcasting.PactSlots : PactSlots { get; init; } = new()`.
  - `CharacterRules.PrimaryCaster(Character) : ClassLevel?`: the first entry with a `SpellcastingAbility`.
  - `CharacterRules.CasterFor(Character, Spell) : ClassLevel?`: the entry whose `Name` matches `spell.Class`
    (case-insensitive, with a `SpellcastingAbility`), else the primary caster.
  - DC and attack overloads, all applying the `spell.dc`/`spell.attack` modifiers. `SpellSaveDc(Character)` and
    `SpellAttackBonus(Character)` keep their names and use the primary caster.

    ```
    SpellSaveDc(Character, ClassLevel) : int?        SpellAttackBonus(Character, ClassLevel) : int?
    SpellSaveDc(Character, Spell) : int?             SpellAttackBonus(Character, Spell) : int?
    ```

  - `SrdCatalog.SpellSlots(IEnumerable<ClassLevel> classes) : int[9]` and `SrdCatalog.PactSlots(int warlockLevel)`
    (unchanged). Delete the old `SpellSlots(CasterType, int)`, or keep it private if it's useful as the single-class
    helper.
  - `Character.ExpendPactSlot() : bool` and `Character.RestorePactSlot()`.

- [ ] **Step 1: Write failing tests**

  RulesTests `SpellSlots_SingleAndMulticlass` (Theory), with `[levels 1..9]` expected:

  | Classes | Expected slots |
  |---|---|
  | Paladin 5 Half | `[4, 2]` |
  | Fighter 7 Third (EK) | `[4, 2]` |
  | Fighter 2 Third | `[]` |
  | Wizard 5 Full | `[4, 3, 2]` |
  | Paladin 2 Half + Sorcerer 3 Full (caster level 1 + 3 = 4) | `[4, 3]` |
  | Warlock 3 Pact + Sorcerer 2 Full (only one non-pact caster, so its own table) | `[3]` |
  | Fighter 11 None + Blood Hunter 1 None | `[]` |

  Other tests:
  - RulesTests `SpellDc_PerClass`: Wizard 5 (INT 18) + Cleric 3 (WIS 14), total level 8, PB +3. A spell with
    `Class = "Cleric"` → DC 13. A spell with a blank class → DC 15 (wizard, the primary caster). A `spell.dc` +1
    modifier adds to both.
  - GameplayTests:
    - `CastSpell_UsesPactSlotOnTie`: pact level 2 with 2 available, regular L2 with 1 available. Casting a level-1
      spell picks the lowest level ≥ 1. If a regular L1 is available, that is used (L1 < L2). With no L1, the tie at
      L2 goes to the pact slot (pact expended 1, regular L2 unchanged). The returned text names `"pact slot"`.
    - `CastSpell_PactSlotTooLowIsSkipped`: pact level 1 with nothing else left, level-2 spell → the "No spell slot"
      message.
    - `ExpendRestorePactSlot` round-trip.
  - RestTests:
    - `ShortRest_RestoresPactSlots_NotRegularSlots`.
    - `LongRest_RestoresPactSlots`.
  - PersistenceTests:
    - `SchemaTwo_PactResourceBecomesPactSlots`: resource `"Pact Magic slots (level 3)"`, max 2, current 1 →
      `PactSlots` (3, 2, expended 1), and the resource is gone. A resource named `"Rage"` is untouched.
    - `SchemaTwo_SpellcastingClassLabelKept`.

- [ ] **Step 2: Run the tests to confirm they fail**

  Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~SpellSlots|FullyQualifiedName~Pact|FullyQualifiedName~SpellDc"`.
  Expected: FAIL.

- [ ] **Step 3: Implement the slot calculation in `SrdCatalog.SpellSlots(IEnumerable<ClassLevel>)`**

  Spec §6:

  ```
  casters = entries with Caster in {Full, Half, Third}
  if casters.Count == 1:  effective = Full: level | Half: level < 2 ? 0 : ceil(level/2) | Third: level < 3 ? 0 : ceil(level/3)
  else:                   effective = Σ Full.level + Σ floor(Half.level/2) + Σ floor(Third.level/3)
  effective 0 → all zeros; else FullCasterSlots[min(effective,20) − 1]
  ```

- [ ] **Step 4: Implement the DC and attack overloads, `CastSpell` and `SpellSlotRecovery`**

  - `CastSpell`: candidates are the regular slots with `Level ≥ minimum && Available > 0`, plus the pact slot when
    `Level ≥ minimum && Available > 0`. Pick the lowest level; on a tie, the pact slot wins. The message is
    `"Cast X using a level N pact slot."` or keeps the existing text.
  - `SpellSlotRecovery`: pact slots recover on any rest, regular slots on a long rest only.

- [ ] **Step 5: Add the pact-resource migration to the `< 3` block**

  Look for the first resource whose `name` matches `^Pact Magic slots \(level (\d)\)`. Copy it to
  `spellcasting.pactSlots` (level from the match, `maximum`, `expended = maximum − current`), then remove it from
  `resources`.

- [ ] **Step 6: Change `ApplyClassDefaults` to set `PactSlots`**

  `ApplyClassDefaults` sets `PactSlots` (level, maximum, expended 0) instead of adding a resource. Update
  `Factory_AppliesClassDefaults` (RestTests): for a Warlock 5, `warlock.Spellcasting.PactSlots` is (3, 2) and
  `Resources` is empty.

- [ ] **Step 7: Add pact pips to the Play view**

  - In the slot area, add one row "Pact (level N)" of pips, visible only when `PactSlots.Maximum > 0`.
  - Clicking a pip calls a new `PlayViewModel.TogglePactSlot` command (`ExpendPactSlot`/`RestorePactSlot`) and logs
    `"Pact slots: a/m available."`.
  - Reuse the `SlotPip` template. A pip list for pact can come from a `CharacterViewModel.PactPips` property built like
    `SpellLevelRow.Pips`.

- [ ] **Step 8: Run all tests**

  Run: `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: everything passes.

- [ ] **Step 9: Update the docs**

  `ARCHITECTURE.md` §5: pact slots recover on any rest, and the hit-dice row is now largest-first.

- [ ] **Step 10: Commit**

  `git commit -am "feat: multiclass spell slots, pact magic slots and per-class spell DC"`.

---

### Task 3: Catalog multiclass data and Blood Hunter

**Files:**

- Modify:
  - `src/Hearthsheet.Core/Content/SrdCatalog.cs`
  - `CharacterParts.cs` (`ClassLevel.Subclass` setter)
  - `NOTICE.md`
- Test: `RulesTests.cs`

**Interfaces:**

- Produces:
  - `ClassDefinition` gains these members, all populated from the spec §5 tables:
    - `(Ability Ability, int Minimum)[][] MulticlassPrerequisites`: alternatives (OR) of requirements (AND);
    - `(ProficiencyCategory Category, string Name)[] MulticlassProficiencies`;
    - `string[] MulticlassChoices`, e.g. `["one skill", "one musical instrument"]`.

    Use exactly these names:

    | Category | Names |
    |---|---|
    | Armor | `"Light armor"`, `"Medium armor"`, `"Shields"` |
    | Weapon | `"Simple weapons"`, `"Martial weapons"`, `"Shortswords"` |
    | Tool | `"Thieves' tools"`, `"Alchemist's supplies"` |

  - New catalog entry, in alphabetical order in `Classes`:

    ```
    new("Blood Hunter", 10, [Dexterity, Intelligence], null, CasterType.None,
        prereq [[(Int,13),(Str,13)], [(Int,13),(Dex,13)]],
        profs Light armor, Medium armor, Shields, Simple weapons, Martial weapons, Alchemist's supplies)
    ```

    `Lookups.ClassNames` picks it up automatically.
  - `ClassLevel.Subclass` setter: when the new value is "Eldritch Knight" or "Arcane Trickster" (trimmed,
    case-insensitive) and `Caster == None`, set `Caster = Third`, and set `SpellcastingAbility` to Intelligence if it's
    null.

- [ ] **Step 1: Write failing tests**

  - `Catalog_BloodHunterFacts`: `FindClass("blood hunter")` gives HitDie 10, saves {Dex, Int}, Caster None, and 6
    multiclass proficiencies including (Tool, "Alchemist's supplies").
  - `Catalog_EveryClassHasPrerequisites`: all 13 classes have a non-empty `MulticlassPrerequisites`.
  - `Catalog_MonkNeedsDexAndWis`: one alternative with both entries at 13.
  - `Subclass_EldritchKnight_MakesThirdCaster`: covers the auto-set rule, and also shows that an entry with
    `Caster = Half` stays Half.

- [ ] **Step 2: Run the tests to confirm they fail**

  Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~Catalog_|FullyQualifiedName~Subclass_"`.
  Expected: FAIL.

- [ ] **Step 3: Implement the catalog data and the setter**

  Check every row in spec §5 against SRD 5.1 "Multiclassing" while you type them in.

- [ ] **Step 4: Add the `NOTICE.md` section**

  Add a "Blood Hunter" section under "What the app contains". It must say:
  - Blood Hunter is a class by Matt Mercer / Critical Role;
  - Hearthsheet includes only its name, hit die, saving-throw proficiencies, multiclass prerequisites and multiclass
    proficiencies;
  - no text from the class is reproduced;
  - Hearthsheet is not affiliated with or endorsed by Critical Role.

- [ ] **Step 5: Run all tests**

  Run: `dotnet test Hearthsheet.slnx`. Expected: everything passes.

- [ ] **Step 6: Commit**

  `git commit -am "feat: multiclass prerequisites and proficiencies, Blood Hunter class facts"`.

---

### Task 4: Level up and multi-entry "Apply class defaults" (Core)

**Files:**

- Modify:
  - `src/Hearthsheet.Core/Content/SrdCatalog.cs` (`CharacterFactory`)
  - `src/Hearthsheet.Core/Rules/CharacterRules.cs` (make proficiency matching reusable)
- Test: create `tests/Hearthsheet.Tests/LevelUpTests.cs` (a new class keeps RulesTests readable)

**Interfaces:**

- Consumes: Tasks 1–3.
- Produces:

  ```csharp
  public sealed record LevelUpOptions(int? Roll = null, int? HitDie = null, CasterType? Caster = null, Ability? Ability = null);
  public sealed class LevelUpReport { public bool Completed { get; set; } = true; public List<string> Changes { get; } = [];
                                      public List<string> Warnings { get; } = []; public List<string> NeedsAttention { get; } = []; }
  public static LevelUpReport CharacterFactory.LevelUp(Character c, string className, LevelUpOptions options);
  public static bool CharacterRules.HasProficiency(Character c, ProficiencyCategory category, string name);
  public static void CharacterFactory.ApplyClassDefaults(Character c);   // multi-entry; replaces (Character, ClassDefinition)
  ```

  - `Roll == null` means take the average.
  - `HitDie`, `Caster` and `Ability` are used only when the class is new and not in the catalog.
  - `HasProficiency` uses the existing `SplitList` + `ProficiencyKey` normalization for every category, so "Simple"
    matches "Simple weapons" and "Light armor" matches "light Armor".

- [ ] **Step 1: Write failing tests in `LevelUpTests`**

  - `LevelUp_FighterIntoBloodHunter`:
    - **Given:** Fighter 11 (d10, all dice left), STR 16, INT 10, CON 14, MaxHp 100, CurrentHp 100.
    - `LevelUp(c, "Blood Hunter", new())` gives:
      - a second entry, Blood Hunter 1 / d10 / 1 die left;
      - MaxHp 108 (6 + 2) and CurrentHp 108;
      - one warning naming Intelligence 13;
      - new proficiencies "Alchemist's supplies", plus armor and weapons not already present;
      - `NeedsAttention` containing `"Add Blood Hunter 1 features by hand."`.
  - `LevelUp_ExistingClass_IncrementsAndChecksNoPrereqs`: Fighter 11 → 12 with no warnings, and the dice count rises by
    1.
  - `LevelUp_PrereqCheckedForExistingClassesToo`: Wizard 3 with INT 12, levelling into Fighter with STR 14 → a warning
    naming Wizard / Intelligence.
  - `LevelUp_DoesNotDuplicateProficiencies`: an existing weapon entry "Simple, Martial" means no "Simple weapons" or
    "Martial weapons" is added.
  - `LevelUp_SkillChoiceGoesToNeedsAttention`: into Rogue → `NeedsAttention` contains `"Choose one skill (Rogue multiclass)."`.
  - `LevelUp_RollUsed_AndValidated`:
    - `new(Roll: 9)` on a d10 → +9 + CON;
    - `Roll: 11` → `ArgumentOutOfRangeException`;
    - a CON −3 roll of 1 → +1 (minimum 1).
  - `LevelUp_AtTwenty_Refused`: `Completed == false`, nothing changes, and `NeedsAttention` says level 20.
  - `LevelUp_CustomClass`: "Witch" with `HitDie: 6, Caster: Full, Ability: Charisma` → an entry with those values. No
    prerequisite warnings and no proficiencies; `NeedsAttention` includes "Add Witch proficiencies by hand."
  - `LevelUp_NonCasterKeepsHandEnteredSlots`: Fighter 11 with hand-set L1 max 3 and `PactSlots` (2, 2, expended 1),
    into Blood Hunter → all of those stay unchanged.
  - `LevelUp_CasterRecomputesSlotsKeepsExpended`: Wizard 4 (L1 4/expended 2, L2 3/expended 3) → Wizard 5 gives L3 max
    2, L1 expended still 2, L2 expended 3.
  - `ApplyClassDefaults_MultiEntry`:
    - **Given:** Fighter 11 (Caster Third, Subclass EK) + Blood Hunter 1, CON +2.
    - **Expect:**
      - saves STR and CON only (from the first entry);
      - MaxHp = 10 + 2 + 10 × (6 + 2) + 1 × (6 + 2) = 100;
      - every entry has full hit dice;
      - Fighter's Caster is still Third;
      - slots for a single third caster at level 11 (effective 4 → `[4, 3]`).
  - `ApplyClassDefaults_CustomFirstClassAllowed`: a "Witch" (d6) first entry is used as stored, with no exception.

- [ ] **Step 2: Run the tests to confirm they fail**

  Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~LevelUpTests"`. Expected: compile failure.

- [ ] **Step 3: Implement `LevelUp`, following spec §7.1 steps 1–7 in order**

  - Prerequisite check: an alternative is met when all its `(ability, min)` satisfy `CharacterRules.AbilityScore(c, a)
    ≥ min`. Warning text: `"<Class> multiclassing needs <Intelligence 13>."`, with "or" between alternatives.
  - Recompute slots only under the spec §6 "recompute only what has a source" rule. Clamp `Expended` to the new
    `Maximum`.
  - `CharacterFactory.Create` keeps using `ApplyClassDefaults` for the single first entry.

- [ ] **Step 4: Implement multi-entry `ApplyClassDefaults(Character)`**

  Follow spec §7.2. The catalog fills `HitDie`, `Caster` and `SpellcastingAbility` only when a built-in entry's value
  is default or blank:
  - `HitDie` is still the default 8 and the catalog die differs;
  - `Caster` is None and the catalog's isn't;
  - the ability is null.

  The `CharacterViewModel.ApplyClassDefaults` command now calls this. Drop the "not a built-in class" refusal and update
  the confirm text to say it applies to every class entry.

- [ ] **Step 5: Run all tests**

  Run: `dotnet test Hearthsheet.slnx`. Expected: everything passes, including the updated `Factory_AppliesClassDefaults`.

- [ ] **Step 6: Commit**

  `git add tests/Hearthsheet.Tests/LevelUpTests.cs && git commit -am "feat: level up into new or existing classes"`.

---

### Task 5: Details and Play: Classes table, Level up dialog, short-rest die picker

**Files:**

- Create: `src/Hearthsheet.App/Views/LevelUpDialog.xaml(.cs)`
- Modify:
  - `Dialogs.cs` (`LevelUp`)
  - `CharacterViewModel.cs` (`AddClass` and `MoveClass` commands, `RemoveEntry` handles `ClassLevel`, `LevelUp`
    command)
  - `DetailsView.xaml` (Classes table, spells Class column, pact fields)
  - `PlayView.xaml` / `PlayViewModel.cs` (Level up button, summary)
  - `ShortRestDialog.xaml(.cs)` (die picker)
- Test: no unit tests (App isn't referenced by tests). Verify by running the app (Step 4).

**Interfaces:**

- Consumes:
  - `CharacterFactory.LevelUp`, `LevelUpOptions`, `LevelUpReport` (Task 4);
  - `HitDicePools`, `SpendHitDie(size, roll)`, `ClassSummary` (Task 1);
  - `PactSlots` (Task 2).
- Produces:
  - `Dialogs.LevelUp(Character c) : (string ClassName, LevelUpOptions Options)?`.
  - `CharacterViewModel.LevelUp : ICommand`. It calls the dialog, then `CharacterFactory.LevelUp`, shows the report
    with `_dialogs.Info` (changes, then "Check:" for warnings and needs-attention), and logs it.

- [ ] **Step 1: Details**

  Replace the Class / Subclass / Level fields with a Classes `DataGrid`, styled like the existing tables. Columns:
  - Class: editable combo over `Lookups.ClassNames`;
  - Subclass;
  - Level;
  - Hit die: combo over `HitDieSizes`, formatted `d{0}`;
  - Dice left;
  - Caster: combo over `CasterType`;
  - Spellcasting ability: `OptionalAbilities`.

  Behaviour:
  - Row 0 shows "Starting class".
  - The hover remove button is collapsed when `Classes.Count == 1`.
  - Move up/down buttons call `MoveClass` (`Classes.Move`).
  - Next to the table: "Add class…" (opens the level-up dialog with a new class), "Level up…" and
    "Apply class defaults…".
  - Remove the old Hit die / Hit dice remaining fields from the combat row. Keep the "Spellcasting class" text box
    (the label).
  - Add Pact Magic fields (Level, Maximum, Expended) beside the spell slots.
  - Add a Class column to the spells table, visible when two or more entries have a `SpellcastingAbility`
    (`CharacterViewModel.IsMultiCaster`).

  Every input gets an `AutomationProperties.Name` such as "Class 2 level".

- [ ] **Step 2: Level up dialog**

  - **Class combo:** existing entries, then built-ins, editable.
  - **Custom class:** when the typed name matches neither an existing entry nor the catalog, show Hit die, Caster and
    Ability fields.
  - **HP:** a radio choice between "Take average (n)", where n = die/2 + 1 + CON (min 1), and "Roll". The roll has a
    box (1..die) plus a "🎲 Roll for me" button, using the same pattern as `ShortRestDialog`.
  - **Warnings:** shown live, non-blocking, using the same prerequisite check through a small public
    `CharacterFactory.MulticlassWarnings(Character, string className) : IReadOnlyList<string>` extracted from
    `LevelUp`. Add it to Task 4's tests if you extract it then.
  - **Buttons:** OK and Cancel.

- [ ] **Step 3: Play and the short rest dialog**

  - Play: a "Level up…" button in the header actions next to the rest buttons, with a tooltip. The header summary
    already uses `Summary`.
  - When `IsMultiCaster` is true, each prepared spell row shows its own DC from `SpellSaveDc(c, spell)`. Expose it
    through a small `SpellDcText(Spell)`-style converter, or a row wrapper if the spell list needs one.
  - Short rest dialog: a `ComboBox` of pools (`"d10 (9 left)"`) above the roll row, visible when there are two or more
    pools. The roll validation, the "Roll for me" range and `DiceText` use the selected size. Spend is disabled when the
    selected pool has nothing left.

- [ ] **Step 4: Verify by running the app**

  Run the app against a scratch data folder (`Application:DataDirectory` in `appsettings.user.json`; see CLAUDE.md).
  Then check:
  - Create Fighter 11 → Level up → Blood Hunter (average). The warning shows, the summary reads
    "Fighter 11 / Blood Hunter 1", and the hit dice read 12d10.
  - Edit the Blood Hunter row's die to d8 → the short rest shows the picker. Spend a d8; the d10 count is unchanged.
  - Removing the Fighter row is impossible when it's the only row.
  - Tab through the new controls; each one announces a name (UI Automation / Narrator, or Accessibility Insights).

- [ ] **Step 5: Run all tests**

  Run: `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: everything passes.

- [ ] **Step 6: Commit**

  `git add src/Hearthsheet.App/Views/LevelUpDialog.* && git commit -am "feat: classes table, level up dialog, hit die picker"`.

---

### Task 6: Sheet layout C, spell header and hit dice box

**Files:**

- Modify: `src/Hearthsheet.App/Views/SheetView.xaml`, `CharacterViewModel.cs`
- Modify: `docs/ARCHITECTURE.md` §13 (test count), §14 (remove the single-class limitation; note that subclass
  features and Profane Soul slots are manual) and §12 if needed

**Interfaces:**

- Consumes: `ClassSummary`, `HitDicePools`, `PrimaryCaster`, `SpellSaveDc(c, ClassLevel)`, `PactSlots`.
- Produces, on `CharacterViewModel`:
  - `IsMulticlass` (bool, `Classes.Count > 1`);
  - `HitDiceTotalText` ("Total 11d10 + 1d8");
  - `HitDiceRemainingText` ("9d10 · 1d8");
  - `CasterLines : IReadOnlyList<string>`, one `"Wizard · INT · DC 15 · +7"` per casting entry;
  - `PactPips` (if Task 2 didn't already add it).

- [ ] **Step 1: Header**

  - When `IsMulticlass` is false, the existing three fields stay exactly as they are, bound two-way to `FirstClass`.
  - When it's true, replace them with one two-column-wide shaded box (`SheetShadedBox` / the calculated-cell style from
    1.3.1). It shows `ClassSummary` and is labelled "Class & Level · total {TotalLevel}"; the remaining fields keep the
    4×2 grid.
  - Implement the switch with `DataTrigger`s on `IsMulticlass` (two `Visibility`-toggled panels is fine).

- [ ] **Step 2: Hit dice box**

  - One pool: unchanged (editable remaining, "Total 12d10").
  - Several pools: shaded, read-only `HitDiceTotalText` / `HitDiceRemainingText`.

- [ ] **Step 3: Spell page**

  - One caster: unchanged. The ability combo binds to `PrimaryCaster.SpellcastingAbility`, and the "Spellcasting Class"
    box is bound to the label (with the primary caster's name as its placeholder when blank).
  - Several casters: the strip shows `CasterLines` (shaded, read-only) instead of the ability, DC and attack fields.
  - A "Pact Magic" pip row appears only when `PactSlots.Maximum > 0`.

- [ ] **Step 4: Check the printout**

  Run the app and use Print → "Microsoft Print to PDF" for three characters:
  - a single-class Wizard 5, compared visually with a print from `dev` (it must be identical);
  - Fighter 11 (Echo Knight) / Blood Hunter 1;
  - Warlock 3 / Sorcerer 2.

  Nothing may be clipped, and no add/remove controls may be visible.

- [ ] **Step 5: Run all tests, update `ARCHITECTURE.md` §13/§14, commit**

  Run: `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: everything passes.
  Then `git commit -am "feat: multiclass sheet layout and docs"`.

---

### Task 7: Sync check and PR

**Files:** test: `tests/Hearthsheet.Tests/CloudSyncTests.cs`

- [ ] **Step 1: Write a test**

  Write `Pull_SchemaTwoRow_MigratesToClasses`, using the existing in-memory cloud. Put a row with
  `SchemaVersion = 2` and the v2 JSON from Task 1 into the cloud, then pull. Expect the local character to have one
  Fighter 11 entry, and its next save to write `schema_version` 3.

- [ ] **Step 2: Run it**

  Run: `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~CloudSyncTests"`. Expected: PASS. No production
  change should be needed; if it fails, fix the migration, not the sync code.

- [ ] **Step 3: Run the whole suite, then commit and open the PR**

  - Run the full `dotnet test Hearthsheet.slnx`, then commit.
  - Push `feature/multiclassing` and open a PR into **`dev`** with a summary and a test plan. The body ends with the
    Claude Code attribution line. Never target `main`.
