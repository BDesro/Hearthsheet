# Attack roller on Play

## Intent

At the table, working out an attack means adding the to-hit bonus to a d20, then adding up several dice and flat
bonuses split across damage types, and doubling the dice on a crit. The player wants to pick an attack on Play,
enter (or let the app roll) the dice, and have the app do the maths and report the to-hit total and the damage per
type. This is the second of four requested features (after the toolbar and dark mode, before resource rolls).

## Goals

- A new **Attacks** card on Play, in both apps, listing every attack from Details → Combat with its to-hit bonus,
  its damage text and a **Roll** button.
- **Roll** opens a dialog that:
  - takes the **attack roll**: Normal / Advantage / Disadvantage. Normal has one d20 box; advantage or disadvantage
    has two and uses the higher or lower. It shows `d20 + bonus = total`. The attack roll is optional: left blank,
    the dialog only does damage (save-based attacks such as Fire Cone).
  - marks **Critical hit** automatically on a natural 20 (the d20 actually used), and lets the player tick or untick
    it by hand. A natural 1 is shown as "Natural 1: miss" (not colour alone).
  - asks for **one box per dice group** of each damage type, holding the sum of those dice (e.g. one box for "2d6
    fire"). On a crit each group's dice count doubles ("4d6 fire").
  - shows **totals per damage type** and an overall total, live as boxes are filled. Flat bonuses are added by the
    app.
  - has **Roll for me**, which fills every empty dice box and the d20 box(es) with random rolls, like Short rest and
    Level up already do. Typed values are kept.
  - **Done** writes a line to the session log, e.g. `Dragon's Wrath Katana: 26 to hit (crit), 21 slashing + 14 fire`.
    Cancel writes nothing.
- Works for both kinds of attack:
  - **Weapon attacks**: dice and flat bonuses come from the weapon, its effects, the attack's dice override, and
    damage modifiers (Bless-style effects, features, items), exactly as the sheet already computes them.
  - **Free-text attacks**: the damage text is parsed when it's simple (`8d6 Fire`, `1d10 + 3 piercing + 1d6 cold`,
    `2d4+2`). If it can't be parsed, the dialog shows the text and a single "Damage total" box.

## Non-goals

- Applying damage to a target or tracking enemies/monster HP.
- Rules automation beyond the crit doubling: resistance/vulnerability, Great Weapon Fighting rerolls, Brutal Critical,
  Sneak Attack prompts, reach/range, ammunition counts.
- Spell attacks from the Spells list (only what's in Combat → Attacks). A spell with an attack can be added there as
  a free-text attack.
- Persisting rolls: the session log is session-only, as today.

## Anti-goals

- The roller and the sheet disagreeing about an attack's damage. One breakdown feeds both.
- Silently wrong maths from text it half-understood: if any part of a free-text damage line can't be parsed, fall back
  to the single total box rather than guessing.
- Colour-only state (crit, natural 1, out-of-range input).

## Decisions made with the user

- Scope **B**: the dialog covers the to-hit roll and crits too, not damage only.
- Free-text attacks **A**: simple damage text is parsed into per-type boxes; otherwise one total box.
- Approach **A**: one structured damage breakdown in Core feeds both the sheet text and the roller.
- My proposals the user didn't object to: advantage/disadvantage selector, natural 20 auto-ticks Critical (editable),
  optional attack roll, a session log line, one box per dice group (sum), Roll for me.

## Approaches considered

- **A. Structured breakdown (chosen).** `CharacterRules` produces structured damage parts (type, dice terms, flat);
  the existing `DamageRoll` text is formatted from them, and free text is parsed into the same parts. The sheet and
  roller can't disagree; it's all unit-testable; crits double dice counts.
- **B. Re-parse the displayed text.** Less change, but couples the maths to display formatting (the "−" sign, joins);
  a wording tweak could silently break totals. Rejected.

## Design

### Core (`Hearthsheet.Core/Rules`)

- New records: `DiceTerm(int Count, int Sides)` and `DamagePart(string Type, IReadOnlyList<DiceTerm> Dice, int Flat)`.
  `Type` may be empty (untyped).
- `CharacterRules.DamageParts(Character c, Attack attack) : IReadOnlyList<DamagePart>?`: for a weapon attack, the
  same grouping the private `DamageParts` does today (main type first, effects grouped by type, flat effects and
  `DamageBonus` folded in), but structured. For a free-text attack, `DamageText.TryParse(attack.Damage)`. Returns
  null when the dice can't be understood (a weapon whose dice text isn't `NdM`, or unparseable free text).
- `DamageRoll(c, attack)` formats from the parts when there are parts, so today's sheet text is unchanged (existing
  `RulesTests` keep passing). It still returns the raw text when parsing fails.
- `DamageText.TryParse(string) : IReadOnlyList<DamagePart>?`: accepts terms joined by `+` or `-` (also `−`),
  each a dice term `NdM` (N optional = 1) or an integer, optionally followed by a damage-type word; a type word
  ends a group, and terms before the next type word belong to the next group. Case-insensitive type grouping.
  Anything else (sentences, "half on a save", parentheses) → null. Bounds: N 1–100, M one of 2,3,4,6,8,10,12,20,100.
- `AttackMath` (pure functions):
  - `int? D20Used(RollMode mode, int? first, int? second)` (higher/lower; null when not enough rolls entered).
  - `bool IsNatural20/IsNatural1(int? d20Used)`.
  - `IReadOnlyList<DamagePart> ForCritical(IReadOnlyList<DamagePart>)` doubles every dice count, flats unchanged.
  - Totals: per part = entered dice sum + flat; per type = sum of its parts; overall = sum. Missing entries count as
    0 but the total is marked incomplete.
  - `enum RollMode { Normal, Advantage, Disadvantage }`.
- Input validation: a dice box accepts integers in `[Count, Count × Sides]` (after crit doubling); a d20 box 1–20.
  Out-of-range input shows an error message on the box and is excluded from totals.

### Presentation (UI-neutral, shared)

- `AttackRollViewModel(Character c, Attack attack, Random random)`: attack name and bonus; `Mode`; `D20First`,
  `D20Second`; `IsCritical` (set by a natural 20 when the d20 changes, editable); a collection of dice-box rows
  (label like "2d6 fire", min/max, entered value, error); `HasStructuredDamage` (false → a single "Damage total"
  box); computed `ToHitText`, `TypeTotals`, `TotalText`; `RollForMe` command; `LogLine` for Done. `Random` is
  injected so tests are deterministic.
- `PlayViewModel`: `Attacks` list (reuse `AttackRow`s or a light row: name, `AttackBonus`, `DamageRoll`) and a
  `RollAttack` command (parameter: the attack) that opens `IDialogs.AttackRollAsync(AttackRollViewModel)` and, when
  it returns true, appends `LogLine` to the activity log.
- `IDialogs.AttackRollAsync(AttackRollViewModel vm) : Task<bool>` implemented by the desktop `Dialogs` (new
  `AttackRollDialog` window, themed, like `ShortRestDialog`) and the web `WebDialogs` (new Razor dialog like
  `ShortRestDialog.razor`).

### UI

- Play gets an **Attacks** card (placement: the builder's call, near Resources). Each row: name, `+12`, damage text,
  **Roll** (accessible name "Roll Dragon's Wrath Katana"). Empty state: "No attacks yet. Add them in Details →
  Combat."
- The dialog layout follows the sketch from the conversation:

  ```
  Dragon's Wrath Katana                       +12 to hit
  ( Normal | Advantage | Disadvantage )
  Attack roll (d20)  [ 14 ]  → 26 to hit
  [x] Critical hit (dice doubled)
  2d8 slashing       [ 9 ]   (2–16)
  4d6 fire           [ 14 ]  (4–24)
  ─────────────────────────────────
  Slashing 17 · Fire 14 · Total 31
  [Roll for me]                 [Cancel] [Done]
  ```
- Both apps: every input has an accessible name; crit and natural 1 are shown in words; works in both themes.

## Tests

- `DamageText.TryParse`: accepted forms, grouping by type, signs, unknown dice sizes, sentences → null.
- `DamageParts` for weapon attacks matches today's `DamageRoll` strings (existing tests) and exposes the structure.
- `AttackMath`: advantage/disadvantage selection, natural 20/1, crit doubling, totals with flats and negatives.
- `AttackRollViewModel`: natural 20 ticks crit; untick sticks; Roll for me fills only empty boxes within range
  (seeded `Random`); out-of-range input flagged and excluded; unparseable damage → single total box; log line text.

## Settled after the builder check (my calls)

- **One grouping, two outputs.** The existing weapon grouping (main type first, effects grouped by type, flat effects
  and `DamageBonus` folded in) is refactored to produce groups of `(type, dice strings, flat)`. `DamageRoll` formats
  those strings exactly as today, so the sheet text stays identical and existing `RulesTests` pass unchanged.
  `DamageParts` parses each dice string of each group (weapon dice, `attack.Dice` override, effect damage such as
  `1d6+2` or `1d8 + 1d4`) with `DamageText.TryParse` as untyped terms. If any string fails, the whole attack has no
  structured damage and the dialog shows the single total box.
- **Flat-only parts** (no dice) get no box but count in their type's total. An attack whose parts are all flat is
  still structured: no boxes, totals shown at once. The single-box fallback is only for unparseable damage.
- **The fallback box is untyped.** The log prints just its number: `Fire Cone: 24 damage`.
- **Crit tick logic:** when the d20 actually used becomes 20, Critical is ticked and remembered as automatic. If it
  then stops being 20 (edited, cleared, mode changed), an automatic tick is removed. A manual tick or untick clears
  the "automatic" flag and is never changed by later d20 edits.
- **Toggling crit** changes each box's range. Entered values are kept, and any now out of range are flagged and left
  out of the totals.
- **Free-text attacks' to-hit** is the existing `CharacterRules.AttackBonus` (ability, proficiency, bonus,
  modifiers), shown as `+N`/`−N`. No extra handling.
- **Log line forms:**
  - `Katana: 19 to hit, 9 slashing + 5 fire`
  - with a crit: `Katana: 26 to hit (crit), …`
  - with a natural 1: `Katana: natural 1 (miss)`, and no damage is listed
  - with no attack roll: `Fire Cone: 24 fire`
  - with an incomplete damage entry: `(partial)` after the totals

## Left to the builder

- Exact placement of the Attacks card on Play and its look in both apps.
- Whether `PlayViewModel` reuses `AttackRow` or a lighter row type.
- Wording of labels and the log line, within the examples above.
