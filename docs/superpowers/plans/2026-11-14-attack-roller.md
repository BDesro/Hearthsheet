# Attack Roller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Attacks card on Play (desktop and web) whose Roll button opens a dialog that takes the d20 and damage dice (typed or rolled), handles advantage, crits and natural 1s, totals damage per type and logs the result.

**Architecture:** Core gets a structured damage breakdown (`DamagePart`, `DiceTerm`), a free-text parser (`DamageText`) and pure maths (`AttackMath`). `CharacterRules`' weapon grouping is refactored so the sheet's text and the structured parts come from one grouping. Presentation gets a shared `AttackRollViewModel`; each app has a thin dialog over it, opened through `IDialogs.AttackRollAsync`.

**Tech Stack:** .NET 10, WPF, Blazor WebAssembly, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-14-attack-roller-design.md`

## Global Constraints

- No new packages. No schema change: nothing new is persisted (the session log is session-only).
- The sheet's damage text must not change: existing `RulesTests` and `PersistenceTests` assertions on `DamageRoll` stay as they are.
- Dice sides accepted: 2, 3, 4, 6, 8, 10, 12, 20, 100. Dice count: 1–100.
- Every WPF input has `AutomationProperties.Name`; crit, natural 1 and out-of-range are shown in words, not colour alone.
- Theme brushes: `DynamicResource` only (ThemeTests enforce this on the toolbar branch; follow it here too). No hard-coded colours.
- Branch `feature/attack-roller` (from `dev`); PR into `dev` at the end. Never push to `dev` or `main`.

## Review Focus

1. **An attack whose weapon was deleted** (orphaned `WeaponId`): it falls back to the attack's own free text, exactly as `DamageRoll` does today. The roller must not crash. Test in Task 2.
2. **A weapon with blank dice but flat effects** ("+1" only): structured, zero dice boxes, totals shown straight away. Test in Task 2.
3. **Advantage chosen with only one d20 entered:** no to-hit total yet and no crash. Switching back to Normal uses the first box. Test in Task 3.
4. **Junk in a box on the web** (empty, letters, negative, 999): flagged or ignored and left out of the totals, never an exception. Test in Task 4, on the view model's string input.
5. **A character with no attacks:** the card shows the empty-state hint and no Roll buttons. Manual check in Tasks 5 and 6.

---

### Task 1: Damage parts and the free-text parser

**Files:**
- Create: `src/Hearthsheet.Core/Rules/Damage.cs`
- Test: `tests/Hearthsheet.Tests/DamageTextTests.cs`

**Interfaces:**
- Produces, in namespace `Hearthsheet.Core.Rules`:
  - `public sealed record DiceTerm(int Count, int Sides)`
  - `public sealed record DamagePart(string Type, IReadOnlyList<DiceTerm> Dice, int Flat)` (Type "" = untyped)
  - `public static class DamageText { public static IReadOnlyList<DamagePart>? TryParse(string? text); }`
- Parsing rules:
  - Terms are joined by `+`, `-` or `−`. Each term is `NdM` (N optional, meaning 1) or an integer, optionally followed by one damage-type word (letters only).
  - A type word closes the group it ends. Groups with the same type, ignoring case, merge, keeping the first one's spelling.
  - Terms after the last type word form an untyped group, and so does the whole text when it has no type words.
  - Whitespace is optional. Empty or whitespace text returns an empty list, not null.
  - Anything else returns null: words that aren't the last token of a term, parentheses, `d7`, `0d6`, `101d6`, a minus before a dice term.

- [ ] **Step 1: Write the failing tests:**
  - `TryParse("8d6 Fire")` → one part: Type "Fire", Dice `[8d6]`, Flat 0.
  - `"1d10 + 3 piercing + 1d6 cold"` → `[piercing: 1d10, +3]`, `[cold: 1d6, 0]`.
  - `"2d4+2"` → `[(""): 2d4, +2]`.
  - `"d20"` → `[(""): 1d20]`.
  - `"1d8 + 1d4 slashing + 2 slashing"` → one part slashing `[1d8, 1d4]`, Flat 2.
  - `"1d6 − 1 fire"` → fire `[1d6]`, Flat −1.
  - `""` and `"   "` → empty list.
  - Each of `"30-ft cone. Save DC 16"`, `"1d7"`, `"0d6"`, `"101d6"`, `"(1d6) fire"` and `"2d6 fire damage"` → null.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~DamageTextTests"`. Expected: build FAIL (types missing).
- [ ] **Step 3: Implement** `Damage.cs`: a regex tokenizer over terms, returning null on the first token that doesn't fit.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** `Add structured damage parts and free-text damage parser`.

### Task 2: Structured damage for attacks (one grouping, two outputs)

**Files:**
- Modify: `src/Hearthsheet.Core/Rules/CharacterRules.cs:230-262`: the private `DamageParts` becomes `DamageGroups`, and a public `DamageParts(Character, Attack)` is added
- Test: `tests/Hearthsheet.Tests/RulesTests.cs` (new tests beside the existing `DamageRoll` tests around line 200)

**Interfaces:**
- Consumes: `DamagePart`, `DiceTerm`, `DamageText` (Task 1).
- Produces: `public static IReadOnlyList<DamagePart>? CharacterRules.DamageParts(Character c, Attack attack)`.
  - **Weapon attacks:** take the groups from `DamageGroups(c, w, dice)` (dice = `attack.Dice` when set, otherwise `w.Damage`). Parse each group's dice strings with `DamageText.TryParse`. A piece may yield only parts that are untyped or of the group's own type. Merge their dice and flats into the group, with the group's flat added once. Return null if any piece fails.
  - **Non-weapon attacks, including orphaned ones:** `DamageText.TryParse(attack.Damage)`.
- `DamageRoll` and `AdditionalDamage` keep their output exactly, now formatted from `DamageGroups`.

- [ ] **Step 1: Write the failing tests:**
  - **The existing sword:** "1d8 + 4 slashing + 2d6 fire" gives `DamageParts` of `[slashing: 1d8, +4]`, `[fire: 2d6, 0]`.
  - **Dice override `1d10`:** gives slashing `[1d10]`.
  - **An orphaned weapon attack** (the existing "ignored" case) → null.
  - **A free-text attack** with Damage "8d6 Fire" → fire `[8d6]`.
  - **An effect with damage `1d6+2`** and type cold → cold `[1d6]`, Flat 2.
  - **Weapon dice blank plus a flat effect "1":** parts `[slashing: no dice, Flat = DamageBonus]` (Review Focus 2).
  - **Weapon dice "1d8 (versatile)"** → null.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~RulesTests"`. Expected: the new tests FAIL to compile.
- [ ] **Step 3: Implement** the refactor and `DamageParts` per the interface.
- [ ] **Step 4: Run** `dotnet test Hearthsheet.slnx`. Expected: all pass, including the unchanged `DamageRoll` assertions in `RulesTests` and `PersistenceTests`.
- [ ] **Step 5: Commit** `Structured damage parts for attacks from the same grouping as the sheet`.

### Task 3: Attack maths

**Files:**
- Create: `src/Hearthsheet.Core/Rules/AttackMath.cs`
- Test: `tests/Hearthsheet.Tests/AttackMathTests.cs`

**Interfaces:**
- Produces, in `Hearthsheet.Core.Rules`:
  - `public enum RollMode { Normal, Advantage, Disadvantage }`
  - `public static class AttackMath`:
    - `int? D20Used(RollMode mode, int? first, int? second)`: Normal uses first; the other modes need both and take the max or min.
    - `IReadOnlyList<DamagePart> ForCritical(IReadOnlyList<DamagePart> parts)`: doubles every `DiceTerm.Count`.
    - `(int Min, int Max) Range(DamagePart part)`: the sum of `Count` and of `Count × Sides`.
    - `int RollDice(DamagePart part, Random random)`: rolls every die.
    - `IReadOnlyList<(string Type, int Total)> Totals(IReadOnlyList<DamagePart> parts, IReadOnlyList<int?> diceSums)`: each part's flat plus its sum (a null sum counts as 0), grouped by type ignoring case, in first-seen order.

- [ ] **Step 1: Write the failing tests:**
  - `D20Used`: (Normal, 14, null) → 14; (Advantage, 3, 17) → 17; (Disadvantage, 3, 17) → 3; (Advantage, 3, null) → null (Review Focus 3); (Normal, null, 9) → null.
  - `ForCritical([2d6 fire +3])` → `[4d6 fire +3]`.
  - `Range([1d8, 1d4] slashing)` → (2, 12).
  - `RollDice` with `new Random(1)` stays within `Range` over 100 runs.
  - `Totals` for `[slashing 1d8 +4]` and `[fire 2d6]` with sums [5, 7] → `[("slashing", 9), ("fire", 7)]`; with sums [null, 7] → `[("slashing", 4), ("fire", 7)]`.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~AttackMathTests"`. Expected: build FAIL.
- [ ] **Step 3: Implement** `AttackMath.cs`.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** `Attack maths: advantage, crits, ranges, totals`.

### Task 4: Shared view model, dialog contract and Play command

**Files:**
- Create: `src/Hearthsheet.Presentation/AttackRollViewModel.cs`
- Modify: `src/Hearthsheet.Presentation/IDialogs.cs` (add the method), `src/Hearthsheet.Presentation/PlayViewModel.cs` (`Attacks`, `RollAttack`), `tests/Hearthsheet.Tests/CharacterViewModelTests.cs` and `tests/Hearthsheet.Tests/ShellViewModelTests.cs` (the `FakeDialogs` classes implement the new member), `src/Hearthsheet.App/Dialogs.cs` and `src/Hearthsheet.Web/WebDialogs.cs` (a stub `Task.FromResult(false)` so the build stays green; replaced in Tasks 5 and 6)
- Test: `tests/Hearthsheet.Tests/AttackRollViewModelTests.cs`

**Interfaces:**
- Consumes: `CharacterRules.DamageParts`, `CharacterRules.AttackBonus`, `CharacterRules.DamageRoll`, `AttackMath`, `RollMode`.
- Produces:
  - `public sealed class DamageBox : Observable`:
    - `DamagePart Part`, `string Label` (e.g. "2d6 fire"; "4d6 fire" on a crit; a flat part's label includes its flat, e.g. "1d8 + 4 slashing"), `int Min`, `int Max`.
    - `string Input` (the raw text typed), `int? Value` (parsed and in range, else null), `string? Error` (e.g. "Between 2 and 12").
  - `public sealed class AttackRollViewModel : Observable`, constructed as `AttackRollViewModel(Character c, Attack attack, Random random)`:
    - Attack details: `string Name`, `int AttackBonus`, `string BonusText` ("+12").
    - The d20: `RollMode Mode`, `bool HasSecondD20`, `string D20First`, `string D20Second` (string inputs), `string? D20Error`.
    - `bool IsCritical` (get/set, with the automatic tick logic below).
    - `IReadOnlyList<DamageBox> Boxes` (dice parts only; flat-only parts aren't boxes but count in totals).
    - The fallback: `bool HasStructuredDamage`, `string RawDamage`, `string FallbackInput`, `int? FallbackValue`.
    - Results: `string ToHitText` ("14 + 12 = 26 to hit", "Natural 1: miss", "Natural 20: critical hit, 32 to hit", or "" when the d20 is blank or incomplete), `string TotalsText` ("Slashing 9 · Fire 7 · Total 16"), `bool IsComplete`.
    - `ICommand RollForMe`: fills only empty or invalid boxes, the d20s included.
    - `string LogLine`.
  - **Crit logic:** when `D20Used` changes to 20, set `IsCritical` and mark it automatic. When it changes away from 20 while the tick is automatic, clear it. Setting `IsCritical` from outside clears the automatic mark. Toggling crit rebuilds each box's label and range from `ForCritical`, keeping `Input`, so an out-of-range value gets an `Error`.
  - **`LogLine` forms** (from the spec):
    - `"{Name}: 19 to hit, 9 slashing + 5 fire"`
    - `"{Name}: 26 to hit (crit), …"`
    - `"{Name}: natural 1 (miss)"`
    - `"{Name}: 24 fire"` (no d20)
    - `"{Name}: 24 damage"` (fallback)
    - `" (partial)"` appended when any box is empty or invalid
    - Untyped totals print as "damage".
  - `IDialogs.AttackRollAsync(AttackRollViewModel vm) : Task<bool>` (true means Done).
  - `PlayViewModel.Attacks : IEnumerable<AttackRow>` (rows over `Character.Attacks`; `Refresh()` raises it too).
  - `PlayViewModel.RollAttack : ICommand` (parameter `Attack`; `RelayCommand(Func<object?, Task>)`). It builds the view model with `Random.Shared`, awaits the dialog, and on true calls `Record(vm.LogLine)`.

- [ ] **Step 1: Write the failing tests** (`new Random(1)` throughout; the character built like the existing sword tests):
  - **Crit and to-hit text:** d20 "20" → `IsCritical` is true and `ToHitText` starts "Natural 20".
  - **Auto crit clears:** then d20 "12" → `IsCritical` is false.
  - **A manual untick sticks:** d20 "20", then set `IsCritical` false, then d20 "20" again → still false.
  - **Mode switch:** Mode Advantage with only the first box → `ToHitText` is ""; Mode back to Normal → it shows a total (Review Focus 3).
  - **Junk input** (Review Focus 4): box inputs "", "abc", "-3" and "999" → `Value` null, an `Error` set except for "", and totals that leave the box out.
  - **Crit doubles a box:** `IsCritical` true → the fire box `Label` "4d6 fire", `Max` 24; an entered "20" stays valid.
  - **Unticking crit** with "20" entered → `Error` set.
  - **Roll for me:** fills only empty boxes and keeps typed ones.
  - **Fallback:** a free-text "30-ft cone. Save DC 16. Half damage" → `HasStructuredDamage` false; `FallbackInput` "24" → `LogLine` "Fire Cone: 24 damage".
  - **`LogLine`:** one test for each form listed above.
  - **`PlayViewModel.RollAttack`** with a `FakeDialogs` that returns true → the `ActivityLog`'s first entry ends with the `LogLine`; with false → nothing is logged.
- [ ] **Step 2: Run** `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~AttackRollViewModelTests"`. Expected: build FAIL.
- [ ] **Step 3: Implement** the view model, the `IDialogs` member, the `PlayViewModel` members, the fake dialogs and the two app stubs.
- [ ] **Step 4: Run** `dotnet test Hearthsheet.slnx`. Expected: all pass.
- [ ] **Step 5: Commit** `Attack roll view model and Play command`.

### Task 5: Desktop dialog and Attacks card

**Files:**
- Create: `src/Hearthsheet.App/Views/AttackRollDialog.xaml` + `.xaml.cs`
- Modify: `src/Hearthsheet.App/Dialogs.cs` (replace the stub: `new AttackRollDialog { Owner = Owner, DataContext = vm }.ShowDialog() == true`), `src/Hearthsheet.App/Views/PlayView.xaml` (an Attacks card at the top of column 2, above Resources)

**Interfaces:**
- Consumes: `AttackRollViewModel`, `DamageBox`, `PlayViewModel.Attacks` and `RollAttack` (Task 4).

- [ ] **Step 1: The Attacks card.** Title "ATTACKS". Each row shows the name (SemiBold), `+N` to hit, the damage text (InkMuted, wrapping) and a `Roll` button (`AutomationProperties.Name` "Roll {Name}"). The empty state is the Hint "No attacks yet. Add them in Details → Combat." (Review Focus 5).
- [ ] **Step 2: The dialog**, laid out as in the spec's sketch:
  - The mode as three `RadioButton`s.
  - One or two d20 `TextBox`es, with the second shown when `HasSecondD20`.
  - `ToHitText`, and the Critical `CheckBox` bound to `IsCritical`.
  - An `ItemsControl` of `DamageBox`es: the label, a `TextBox` bound to `Input` with `UpdateSourceTrigger=PropertyChanged`, the range "(2–12)", and `Error` in Danger.
  - When the damage isn't structured: the raw damage text and one total box.
  - `TotalsText` in large SemiBold.
  - The buttons: Roll for me, Cancel (`IsCancel`) and Done (`IsDefault`, AccentButton).
  - Every input has an `AutomationProperties.Name`. Use the `AppWindow` style and `DynamicResource` brushes.
- [ ] **Step 3: Build** with `dotnet build Hearthsheet.slnx`. Expected: success.
- [ ] **Step 4: Run the app** on scratch data with sync off (set `Application:DataDirectory` and `Sync:Url` "" in `%LOCALAPPDATA%\Hearthsheet\appsettings.user.json`, which doesn't exist normally, and remove it afterwards). Expected:
  - Play shows the Attacks card.
  - Rolling the Katana with d20 20 auto-ticks crit, doubles the dice and totals per type. Done logs the line.
  - Fire Cone takes a total with no d20.
  - Advantage takes two d20s.
  - The dialog is readable in Light and in Dark. That needs the toolbar branch, so check in light only and note it in the ledger if it isn't merged yet.
  - Screenshot the dialog.
- [ ] **Step 5: Commit** `Desktop attack roll dialog and Attacks card`.

### Task 6: Web dialog and Attacks card

**Files:**
- Create: `src/Hearthsheet.Web/Components/AttackRollDialog.razor`
- Modify: `src/Hearthsheet.Web/WebDialogs.cs` (add `DialogKind.AttackRoll`; `AttackRollAsync` asks with the view model as `Data`, and an answer of `is true` means Done), `src/Hearthsheet.Web/Components/Modal.razor` (render `AttackRollDialog` for that kind), `src/Hearthsheet.Web/Components/PlayView.razor` (an Attacks card before Resources in the same column), `src/Hearthsheet.Web/wwwroot/css/app.css` (only if a layout rule is needed; colours from tokens)

**Interfaces:**
- Consumes: as Task 5.

- [ ] **Step 1: The card and the dialog,** mirroring Task 5 with the existing web patterns (`ShortRestDialog.razor`, the `rows` list in `PlayView.razor`):
  - Inputs use `inputmode="numeric"`, bound with `@bind:event="oninput"` to the string properties.
  - Re-render on the view model's `PropertyChanged`.
  - Labels use `for`/`id`, and the mode is a radio group with a `fieldset`/`legend`.
- [ ] **Step 2: Build** with `dotnet build Hearthsheet.slnx` and run `dotnet publish src/Hearthsheet.Web -c Release -o <scratch>`. Expected: both succeed.
- [ ] **Step 3: Check in the browser** with `preview_start web-debug`. Signing in to the test project needs the user; if they're unavailable, record it as unverified in the ledger and the PR. With sign-in, the same checks as Task 5 Step 4, plus a 375px width.
- [ ] **Step 4: Commit** `Web attack roll dialog and Attacks card`.

### Task 7: Docs and final verification

**Files:**
- Modify: `docs/user/tutorial.md` (add an **Attacks** bullet under "Playing a session")

- [ ] **Step 1: Add the bullet:** "**Attacks**: press **Roll** next to an attack, enter your d20 and damage dice (or **Roll for me**). Hearthsheet adds your bonuses, doubles the dice on a critical hit and totals each damage type, and the result goes in the session log." Run `dotnet test Hearthsheet.slnx --filter "FullyQualifiedName~HelpDocsTests"`. Expected: PASS.
- [ ] **Step 2: Run** `dotnet build Hearthsheet.slnx -c Release` and `dotnet test Hearthsheet.slnx -c Release`. Expected: 0 errors, all pass.
- [ ] **Step 3: Commit** `Docs: attack roller`. Push and open the PR into `dev`, listing what is unverified.
