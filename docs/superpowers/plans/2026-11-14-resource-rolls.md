# Resource Rolls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A resource on Play with a Roll formula opens a roll window on **Use**, applies the total (heal, temporary HP
or just show) and logs it; hovering a resource shows its notes and roll.

**Architecture:** Two new fields on `Resource` (schema 3 → 4). A pure `ResourceFormula` parser/resolver in Core and a
domain method `Character.UseResourceWithRoll`. A shared `ResourceUseViewModel` (reusing the attack roller's
`DamageBox` and `AttackMath`) behind `IDialogs.UseResourceAsync`, shown by a dialog in each app.

**Tech Stack:** .NET 10, WPF, Blazor WebAssembly, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-14-resource-rolls-design.md`

## Global Constraints

- Only authoritative values are stored; the formula is stored as typed text and resolved at use time.
- View models call domain methods; they never mutate the character themselves.
- No new packages. No DI container or MVVM toolkit.
- Every WPF input has `AutomationProperties.Name`; every web input has an `aria-label`. State is never shown by colour alone. Both themes stay readable (`ThemeTests` must pass).
- No hard-coded colours in XAML or CSS (`ThemeTests` enforces this); use existing theme brushes and CSS variables.
- Resources without a Roll behave exactly as today, including the log text `Used {Name} ({Current}/{Maximum} left).` and `{Name} has no uses left.`
- Bundled content gets no formulas (players type their own).
- `.ps1` and `.cmd` stay CRLF. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command for every task: `dotnet test Hearthsheet.slnx` (and `dotnet build Hearthsheet.slnx` must be warning-free of new warnings).

## Review Focus

- **Odd numbers in a formula** (`１d10`, `99999999999`, `0d6`, `1d7`): unreadable, never a crash or overflow. Pinned in Task 2.
- **Class names with spaces** (`blood hunter level`) and stray spacing/case (`  Fighter   LEVEL `): resolve like class names elsewhere. Pinned in Task 2.
- **A negative total** (`WIS` at −1, Heal): applied as 0, shown as rolled, and never damages. Pinned in Task 3.
- **Dangling operators** (`1d10 +`, `+ 1d10`, `1d10 ++ 2`, empty): `+1d10`/`-1` leading sign is fine; dangling or doubled operators are unreadable. Pinned in Task 2.
- **A resource with uses left but Maximum 0 / Current 0 after editing**: no dialog opens and the old "no uses left" line is logged. Pinned in Task 4.

---

### Task 1: Resource fields and schema 4

**Files:**
- Modify: `src/Hearthsheet.Core/Domain/CharacterParts.cs` (class `Resource`, ~line 236)
- Modify: `src/Hearthsheet.Core/Domain/Enums.cs`
- Modify: `src/Hearthsheet.Core/Serialization/CharacterJson.cs:24`
- Modify: `src/Hearthsheet.Presentation/Lookups.cs`
- Test: `tests/Hearthsheet.Tests/PersistenceTests.cs`

**Interfaces:**
- Produces: `enum ResourceResult { JustShow, Heal, TemporaryHp }` (in `Enums.cs`); `Resource.Roll : string` (default `""`, null → `""`); `Resource.Result : ResourceResult` (default `JustShow`); `CharacterJson.CurrentSchemaVersion == 4`; `Lookups.ResourceResults : ResourceResult[]`; `Lookups.Display(ResourceResult)` returning `"Just show"`, `"Heal me"`, `"Temporary HP"`.

- [ ] **Step 1: Write the failing tests** in `PersistenceTests`:
  - `Resource_roll_and_result_round_trip`: a character with a resource `Roll = "1d10 + fighter level"`, `Result = ResourceResult.Heal` survives `Serialize`/`Deserialize(…, CharacterJson.CurrentSchemaVersion)` with both values equal.
  - `Schema3_resource_loads_with_defaults`: `CharacterJson.Deserialize("""{"resources":[{"name":"Rage","maximum":3,"current":2}]}""", 3)` gives one resource with `Roll == ""` and `Result == ResourceResult.JustShow`, name and counts intact. (Check the real JSON property name for the resources list in an existing test or `Serialize` output and use it.)
  - `Current_schema_is_4`: `Assert.Equal(4, CharacterJson.CurrentSchemaVersion)`.
  - The existing "newer schema is refused" tests use `CurrentSchemaVersion + 1`, so they cover v5 unchanged.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~PersistenceTests"`. Expected: build fails (no `Roll`/`ResourceResult`).
- [ ] **Step 3: Implement.** Properties follow the `Notes`/`Recovery` pattern on `Resource`. Bump `CurrentSchemaVersion` to 4 with a comment that v4 adds `Resource.roll`/`result` and needs no migration (missing fields take defaults). Add the Lookups array and `Display` overload beside the `RecoveryTiming` ones.
- [ ] **Step 4: Run** `dotnet test Hearthsheet.slnx`. Expected: all pass (existing tests that deserialize with literal `3` still pass).
- [ ] **Step 5: Commit** `Resource roll and result fields; schema 4`.

### Task 2: ResourceFormula

**Files:**
- Create: `src/Hearthsheet.Core/Rules/ResourceFormula.cs`
- Test: `tests/Hearthsheet.Tests/ResourceFormulaTests.cs`

**Interfaces:**
- Consumes: `DiceTerm(int Count, int Sides)` from `Core/Rules/Damage.cs`; `CharacterRules.TotalLevel(c)`, `CharacterRules.ProficiencyBonus(c)`, `CharacterRules.AbilityModifier(c, Ability)`; `Ability` enum.
- Produces:
  - `public sealed record ResolvedRoll(IReadOnlyList<DiceTerm> Dice, int Flat, string Description);`
  - `public static class ResourceFormula` with
    - `public static ResolvedRoll? TryResolve(Character c, string? formula)`
    - `public static bool IsReadable(string? formula)` (syntax only, no character; true exactly when `TryResolve` would be non-null for some character)
    - `public const string ReadError = "Can't read this roll. Use dice, numbers, level, a class level, proficiency or an ability, joined by + or -.";`

Grammar (case-insensitive, whitespace-tolerant): an optional leading `+`/`-`, then terms separated by `+`, `-` or `−`
(U+2212). Max 20 terms. A term is one of:
- `NdM`: N optional (= 1), N 1–100, M one of 2, 3, 4, 6, 8, 10, 12, 20, 100. ASCII digits only. Reuse `DamageText`'s bounds if they are exposed; otherwise mirror them.
- an integer 0–999, ASCII digits only.
- `level`: `TotalLevel`.
- `<class name> level`: the words before a final `level`, collapsed to single spaces and trimmed, matched against `Identity.Classes` names with `string.Equals(…, OrdinalIgnoreCase)` after trimming (as `CharacterRules` does). Levels of every matching entry are summed; no match → 0.
- `proficiency`: `ProficiencyBonus(c)`.
- `str|dex|con|int|wis|cha` or `strength|…|charisma`: that ability's modifier.
Empty/blank, a dangling or doubled operator, or any other term → null / not readable. A dice term with a `-` in front
is not readable (dice are only added).

Result: `Dice` lists each dice term in order (not merged; the view model groups by die size). `Flat` is the signed sum of
every non-dice term (use `checked` or bounded values; with these bounds overflow can't happen, so no extra code).
`Description` joins terms in order with ` + ` / ` − `; dice as `1d10`; integers as themselves; others as
`{value} ({label})` where label is `level`, `fighter level` (the lowercase text typed, normalized), `proficiency` or
the ability's three-letter upper-case code. A term whose signed value is negative after a `+` shows as ` − {abs}`.
A missing class shows `0 (no fighter levels)`.

- [ ] **Step 1: Write the failing tests** (`ResourceFormulaTests`, build characters the way `RulesTests` does):
  - `Dice_and_number`: `"1d10 + 3"` → Dice `[1d10]`, Flat 3, Description `"1d10 + 3"`.
  - `Fighter_level_on_multiclass`: Fighter 11 / Wizard 2, `"1d10 + fighter level"` → Flat 11, Description `"1d10 + 11 (fighter level)"`.
  - `Class_level_is_case_and_space_insensitive`: `"  1d10+  FIGHTER   Level "` → Flat 11.
  - `Class_name_with_spaces`: class `Blood Hunter` 5, `"blood hunter level"` → Flat 5.
  - `Several_entries_of_one_class_add_up`: two `Fighter` entries 3 and 2 → `fighter level` = 5.
  - `Missing_class_counts_zero`: `"1d10 + paladin level"` → Flat 0, Description `"1d10 + 0 (no paladin levels)"`.
  - `Total_level_and_proficiency`: level 13 character, `"level + proficiency"` → Flat 13 + 5 = 18.
  - `Ability_short_and_long`: CON 16 → `"con"` and `"Constitution"` both Flat 3; Description of `"2d4 + CON"` is `"2d4 + 3 (CON)"`.
  - `Negative_modifier_shows_minus`: WIS 8, `"1d8 + wis"` → Flat −1, Description `"1d8 − 1 (WIS)"`.
  - `Subtraction_and_unicode_minus`: `"2d6 - 1"` and `"2d6 − 1"` → Flat −1.
  - `Leading_sign`: `"+1d6"` → readable; `"-2"` → Flat −2.
  - `Unreadable` theory, each → `TryResolve` null and `IsReadable` false: `""`, `"   "`, `"1d10 +"`, `"1d10 ++ 2"`, `"1d7"`, `"0d6"`, `"101d6"`, `"１d10"`, `"99999999999"`, `"1000"`, `"heal 1d10"`, `"1d10 fire"`, `"- 1d6"`, `"(1d6)"`, a 21-term formula.
  - `Readable_without_character`: `IsReadable("1d10 + fighter level")` true.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~ResourceFormulaTests"`. Expected: build fails.
- [ ] **Step 3: Implement** `ResourceFormula`: one private parse into a list of `(sign, kind, payload)` tokens shared by `IsReadable` and `TryResolve`.
- [ ] **Step 4: Run** the filter, then `dotnet test Hearthsheet.slnx`. Expected: all pass.
- [ ] **Step 5: Commit** `ResourceFormula: parse and resolve resource rolls`.

### Task 3: Character.UseResourceWithRoll

**Files:**
- Modify: `src/Hearthsheet.Core/Domain/Character.Play.cs` (next to `UseResource`, ~line 131)
- Test: `tests/Hearthsheet.Tests/GameplayTests.cs`

**Interfaces:**
- Consumes: `UseResource(Resource, int)`, `Heal(int) : string`, `GrantTemporaryHitPoints(int)`, `Resource.Result`.
- Produces: `public string UseResourceWithRoll(Resource resource, int total)`.

Behaviour: if `!UseResource(resource)` return `$"{Name} has no uses left."` and apply nothing. Otherwise
`applied = Math.Max(0, total)`, and the return text is `$"Used {Name} ({Current}/{Maximum} left): rolled {total}."`
followed by one space and the outcome: Heal → `Heal(applied)`'s text; TemporaryHp → call
`GrantTemporaryHitPoints(applied)` then `$"Temporary HP now {Combat.TemporaryHp}."`; JustShow → nothing (no trailing
space). (`Name`/`Current`/`Maximum` here are the resource's.)

- [ ] **Step 1: Write the failing tests:**
  - `Roll_heal_spends_and_clamps_at_max`: max HP 20, current 15, Second Wind 1/1 Heal, total 18 → HP 20, Current 0, text `"Used Second Wind (0/1 left): rolled 18. Healed 5 HP."`.
  - `Roll_temp_hp_keeps_higher`: temp HP 10, total 6 → temp stays 10, text ends `"Temporary HP now 10."`; then total 12 on another use → 12.
  - `Roll_just_show_changes_no_hp`: HP and temp unchanged, text `"Used X (n/m left): rolled 7."`.
  - `Roll_with_no_uses_applies_nothing`: Current 0, Heal, total 18 → HP unchanged, text `"Second Wind has no uses left."`.
  - `Negative_total_heals_nothing`: Heal, total −2 → HP unchanged, use spent, text contains `"rolled -2."` and `"No healing."`.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~GameplayTests"`. Expected: build fails.
- [ ] **Step 3: Implement** as described.
- [ ] **Step 4: Run** `dotnet test Hearthsheet.slnx`. Expected: all pass.
- [ ] **Step 5: Commit** `UseResourceWithRoll applies a rolled resource`.

### Task 4: ResourceUseViewModel, IDialogs and PlayViewModel

**Files:**
- Create: `src/Hearthsheet.Presentation/ResourceUseViewModel.cs`
- Modify: `src/Hearthsheet.Presentation/AttackRollViewModel.cs` (`DamageBox` label only)
- Modify: `src/Hearthsheet.Presentation/IDialogs.cs`, `src/Hearthsheet.Presentation/PlayViewModel.cs:44-45`, `src/Hearthsheet.Presentation/Lookups.cs`
- Modify: `src/Hearthsheet.App/Dialogs.cs`, `src/Hearthsheet.Web/WebDialogs.cs` (placeholder `=> Task.FromResult(false)` so the solution builds; Tasks 5 and 6 replace them)
- Modify: the two `FakeDialogs` in `tests/Hearthsheet.Tests/CharacterViewModelTests.cs` and `ShellViewModelTests.cs`
- Test: `tests/Hearthsheet.Tests/ResourceUseViewModelTests.cs`, `tests/Hearthsheet.Tests/CharacterViewModelTests.cs`

**Interfaces:**
- Consumes: `ResolvedRoll`, `ResourceFormula.TryResolve`, `Character.UseResourceWithRoll`, `DamageBox`, `AttackMath.RollDice`, `DamagePart`, `Lookups.Display(ResourceResult)`.
- Produces:
  - `IDialogs.Task<bool> UseResourceAsync(ResourceUseViewModel vm)` (doc: true when the user chose Use).
  - `ResourceUseViewModel(Character c, Resource r, ResolvedRoll roll, Random random)` exposing `Name`, `UsesText` (`"1 / 1 left"`), `FormulaText` (`roll.Description`), `Boxes : IReadOnlyList<DamageBox>`, `RollForMe : ICommand`, `Total : int?`, `ResultText : string`, `CanUse : bool`; raises change notifications for `Total`, `ResultText`, `CanUse` when a box changes.
  - `Lookups.ResourceTooltip(string notes, string roll, ResourceResult result) : string?`.
  - `PlayViewModel.UseResource` keeps its name and parameter (the `Resource`).

Details:
- Boxes: one `DamageBox` per die size, in first-appearance order, built from `new DamagePart("", [new DiceTerm(sumOfCounts, sides)], 0)`. `DamageBox` currently labels untyped parts `"… damage"`; add an optional constructor argument (e.g. `string untypedLabel = "damage"`) and pass `""` here so the label reads `1d10`. Attack labels must not change (existing tests).
- `Total` = sum of box values + `roll.Flat` when every box has a `Value`, else null. No boxes → total ready at once.
- `ResultText` when `Total` is null: `""`. Otherwise, with `applied = Math.Max(0, Total)`: Heal → `$"Heal {applied}"`, TemporaryHp → `$"Gain {applied} temporary HP"`, JustShow → `$"Total {Total}"`.
- `CanUse` = `Total is not null`.
- `RollForMe` fills only boxes whose `Input` is blank, with `AttackMath.RollDice(box.Part, random)` (as the attack roller does).
- `Lookups.ResourceTooltip`: lines are the trimmed notes (if any) and, when roll is not blank, `$"Roll: {roll.Trim()}"` plus `$" ({Display(result)})"` when result is not JustShow, joined by `"\n"`; null when both are empty.
- `PlayViewModel.UseResource` becomes `new RelayCommand(DoUseResource, p => p is Resource)` (same async pattern as `RollAttack`):
  1. Roll blank or `Current == 0` → today's `Act($"Use {r.Name}", …)` with today's exact strings.
  2. `ResourceFormula.TryResolve` null → `Act` that calls `c.UseResource(r)` and logs `$"Used {r.Name} ({r.Current}/{r.Maximum} left). Couldn't read the roll \"{r.Roll.Trim()}\"."`.
  3. Otherwise build the view model with `Random.Shared`; if `await _dialogs.UseResourceAsync(vm)` and `vm.Total is int t` → `Act($"Use {r.Name}", c => c.UseResourceWithRoll(r, t))`. Cancel spends and logs nothing.
- Fakes: `CharacterViewModelTests.FakeDialogs` gets `UseResourceAnswer` and `OnUseResource` like the attack ones; `ShellViewModelTests.FakeDialogs` returns false.

- [ ] **Step 1: Write the failing tests.** `ResourceUseViewModelTests` (seeded `new Random(1)`):
  - `Groups_boxes_by_die_size`: `"1d6 + 2d6 + 1d8 + 3"` → two boxes labelled `"3d6"` (3–18) and `"1d8"` (1–8).
  - `Total_null_until_every_box_valid`: enter `"7"` in a 1d10 box of `"1d10 + fighter level"` (fighter 11) → Total 18, `ResultText` `"Heal 18"` for a Heal resource; `"11"` → Total null, box `Error` `"Between 1 and 10"`, `CanUse` false.
  - `Flat_only_is_ready_at_once`: `"wis"` (WIS 14) → no boxes, Total 2, `CanUse` true.
  - `Roll_for_me_fills_only_empty`: two die sizes, type in one, `RollForMe.Execute(null)` → typed value kept, other box filled within range.
  - `Result_text_forms`: TemporaryHp total 9 → `"Gain 9 temporary HP"`; JustShow → `"Total 9"`; Heal with total −1 → `"Heal 0"`.
  - `Labels_and_header`: `Name` `"Second Wind"`, `UsesText` `"1 / 1 left"`, `FormulaText` `"1d10 + 11 (fighter level)"`, box label `"1d10"`.
  - `Tooltip_text` theory on `Lookups.ResourceTooltip`: notes only; roll only with Heal → `"Roll: 1d10 + fighter level (Heal me)"`; both → two lines; neither → null; JustShow has no suffix.
  In `CharacterViewModelTests` (Play section, using the fake):
  - `Use_without_roll_logs_as_before`: Current goes down by 1, log ends `"Used Rage (2/3 left)."`, dialog not opened.
  - `Use_with_no_uses_left_skips_dialog`: Roll set, Current 0 → dialog not opened, log ends `"Second Wind has no uses left."`.
  - `Use_with_unreadable_roll_spends_and_logs`: Roll `"1d7"` → Current down 1, log contains `"Couldn't read the roll \"1d7\"."`, dialog not opened.
  - `Use_with_roll_cancel_spends_nothing`: answer false → Current unchanged, nothing logged.
  - `Use_with_roll_applies_result`: Heal resource, fake fills the box with `"7"` in `OnUseResource` and answers true, on a fighter 11 at 5/30 HP with Roll `"1d10 + fighter level"` → HP 23, Current down 1, log line from `UseResourceWithRoll`.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~ResourceUseViewModelTests|FullyQualifiedName~CharacterViewModelTests"`. Expected: build fails.
- [ ] **Step 3: Implement** the view model, the `DamageBox` label argument, the Lookups helper, the IDialogs member with placeholders in both apps and both fakes, and the new `UseResource`.
- [ ] **Step 4: Run** `dotnet test Hearthsheet.slnx` and `dotnet build Hearthsheet.slnx`. Expected: all pass, both apps build.
- [ ] **Step 5: Commit** `Resource roll view model and Play wiring`.

### Task 5: Desktop UI

**Files:**
- Create: `src/Hearthsheet.App/Views/ResourceUseDialog.xaml`, `ResourceUseDialog.xaml.cs`
- Modify: `src/Hearthsheet.App/Dialogs.cs` (replace the placeholder, like `AttackRollAsync` at line 60)
- Modify: `src/Hearthsheet.App/Views/DetailsView.xaml` (Resources grid, ~lines 700–702)
- Modify: `src/Hearthsheet.App/Views/PlayView.xaml` (Resources list, ~line 160)
- Add (if no suitable one exists in `src/Hearthsheet.App/Mvvm/`): an `IMultiValueConverter` calling `Lookups.ResourceTooltip`, and an `IValueConverter` returning `ResourceFormula.ReadError` for an unreadable, non-blank roll (null otherwise).

**Interfaces:**
- Consumes: `ResourceUseViewModel` members from Task 4; `Lookups.ResourceResults`, `Lookups.Display`, `Lookups.ResourceTooltip`; `ResourceFormula.IsReadable`, `ResourceFormula.ReadError`.

- [ ] **Step 1: Dialog.** Model it on `AttackRollDialog` (same window chrome, theme brushes, box template with range and error text, Roll for me / Cancel / primary button). Layout per the spec sketch: name + `UsesText`; `Roll {FormulaText}`; one row per box (label, input, `(min–max)`, error); `Total {Total} → {ResultText}` (or just `ResultText` for Just show); buttons **Roll for me**, **Cancel**, **Use** (default, enabled by `CanUse`). Inputs get `AutomationProperties.Name` like `"1d10 roll"`. Title `Use {Name}`.
- [ ] **Step 2: Details columns.** After Amount: **Roll** text column (MinWidth ~170; the cell shows the error via the converter as a tooltip and a visible `⚠` text marker so it isn't colour alone, with the message as the accessible help text), and **Result** `DataGridComboBoxColumn` over `Lookups.ResourceResults` shown with `Lookups.Display` (follow how Recovers displays its enum).
- [ ] **Step 3: Play tooltip.** `ToolTip` on each resource row via the multi-converter on `Notes`, `Roll`, `Result`; null hides it. `ToolTipService.ShowOnDisabled` not needed.
- [ ] **Step 4: Run** `dotnet build Hearthsheet.slnx` and `dotnet test Hearthsheet.slnx`. Expected: pass, including `ThemeTests`.
- [ ] **Step 5: Manual check** on scratch data with sync off (`Application:DataDirectory` + `Sync:Url` `""` in `%LOCALAPPDATA%\Hearthsheet\appsettings.user.json`; delete it afterwards): set Second Wind `1d10 + fighter level`, Heal; Use → dialog, enter 7, Use → HP and log correct; tooltip shows; a typo shows the Details marker and Use just spends. Check dark mode.
- [ ] **Step 6: Commit** `Desktop: resource roll dialog, Details columns, Play tooltips`.

### Task 6: Web UI

**Files:**
- Create: `src/Hearthsheet.Web/Components/ResourceUseDialog.razor`
- Modify: `src/Hearthsheet.Web/WebDialogs.cs` (replace the placeholder, like `AttackRollAsync` at line 78), and the component that hosts `AttackRollDialog` if dialogs are registered there
- Modify: `src/Hearthsheet.Web/Components/DetailsView.razor` (Resources table, ~lines 460–480)
- Modify: `src/Hearthsheet.Web/Components/PlayView.razor` (~line 124)
- Modify: `src/Hearthsheet.Web/wwwroot/css/*` only if a new class is needed (CSS variables only)

**Interfaces:**
- Consumes: as Task 5.

- [ ] **Step 1: Dialog** modelled on `AttackRollDialog.razor`, same layout and wording as the desktop dialog; inputs have `aria-label`s; errors as text.
- [ ] **Step 2: Details:** a Roll `<input aria-label="@name roll">` with the `ReadError` message shown as a small text line under it when unreadable (linked with `aria-describedby`), and a Result `<select aria-label="@name result">` over `Lookups.ResourceResults` with `Lookups.Display`; add both header cells.
- [ ] **Step 3: Play:** `title` on the resource row from `Lookups.ResourceTooltip`, plus a visually hidden description (`aria-describedby`, using the existing visually-hidden class if there is one) so screen readers get it; nothing rendered when null.
- [ ] **Step 4: Run** `dotnet build Hearthsheet.slnx`, `dotnet test Hearthsheet.slnx`, `dotnet publish src/Hearthsheet.Web -c Release`. Expected: all succeed.
- [ ] **Step 5: Commit** `Web: resource roll dialog, Details columns, Play tooltips`.

### Task 7: Player docs

**Files:**
- Modify: `docs/user/tutorial.md` (Resources bullet under "Playing a session")
- Test: `tests/Hearthsheet.Tests/HelpDocsTests.cs` (existing; must keep passing)

- [ ] **Step 1:** Extend the Resources bullet: in Details → Resources, a resource can have a **Roll** (e.g. `1d10 + fighter level`; dice, numbers, `level`, a class level, `proficiency` or an ability) and a **Result** (Heal me, Temporary HP, Just show). **Use** then opens a roll window; hovering a resource on Play shows its notes and roll.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx`. Expected: pass.
- [ ] **Step 3: Commit** `Docs: resource rolls in the tutorial`.
