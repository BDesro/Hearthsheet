# Resource rolls and descriptions on Play

## Intent

Some class resources do something numeric when used: Second Wind heals 1d10 + fighter level. Today **Use** on Play only
spends a use, and the player works out and applies the number themselves. The player wants **Use** to open a small
roll window when the resource has a number attached, apply the result where it makes sense, and otherwise spend the
use exactly as now. Hovering a resource should show what it is. Each player sets this up for their own resources.
This is the last of four requested features (after the toolbar and dark mode, and the attack roller).

## Goals

- Each resource gets two optional settings, edited in Details → Resources:
  - **Roll**: a formula such as `1d10 + fighter level`. Blank means no roll, and Use behaves exactly as today.
  - **Result**: what the rolled total does: **Heal me**, **Temporary HP**, or **Just show** (the default).
- **Use** on Play, for a resource with a Roll and uses left, opens a dialog:
  - It shows the formula resolved for this character, e.g. `1d10 + 11 (fighter level)`.
  - It has one box per dice group (sum of those dice) with its range, plus **Roll for me**, reusing the attack
    roller's dice boxes. Flat parts are added by the app.
  - It shows the total and what will happen ("Heal 18", "Gain 18 temporary HP", "Total 18").
  - **Use** spends one use, applies the result with the existing rules (`Character.Heal`,
    `Character.GrantTemporaryHitPoints`) and logs a line, e.g. `Second Wind: rolled 18, healed 18 (125/125)`.
    **Cancel** spends nothing.
- With no uses left, Use behaves as today ("no uses left" in the log) and no dialog opens.
- **Tooltip** on each resource on Play (both apps): its Notes, plus `Roll: <formula>` and the result when set. With
  neither, there's no tooltip.
- A formula that can't be read shows a message next to the field in Details. On Play, Use then just spends the use and
  logs that the roll couldn't be read, so a typo never blocks play.

## Non-goals

- Pools (spend N at once, e.g. Lay on Hands, ki): Use keeps spending one; pools use the existing −/+ buttons.
- Targeting other creatures, or damage results (attacks belong in the attack roller).
- Bundled formulas for built-in class features (players type their own; content rules in `NOTICE.md` also apply).

## Anti-goals

- Silently wrong numbers from a half-read formula: unreadable formulas never roll.
- Changing behaviour for resources without a Roll.
- Colour-only state; inaccessible inputs.

## Decisions made with the user

- Result **B**: each resource says what its result does (Heal me / Temporary HP / Just show).
- Pools **A**: one use per press; no "how many" prompt.
- Formula **A**: a typed formula resolved against the character at use time, not pickers.
- Schema **B**: every save from the new build is v4 (the existing rule), not only characters using the new fields. An
  install still on 1.9.x is told to update before it can open or sync any character the new build has saved.
- My proposals the user didn't object to: formula terms `level`, `<class> level`, `proficiency`, ability names; the
  tooltip shows Notes plus the roll; the error shows in Details.

## Approaches considered

- **A. Typed formula (chosen).** One text field, flexible (`2d4 + 2 + WIS`), matches how features read. Typos are
  caught by an error message.
- **B. Pickers** (dice count, die, one "plus" dropdown). No typos, but more columns in an already wide table and only
  one bonus term. Rejected.

## Design

### Core

- `Resource` gains `string Roll` (default "") and `ResourceResult Result` (default `JustShow`), with
  `enum ResourceResult { JustShow, Heal, TemporaryHp }`. Both are observable like the other fields.
- **Schema:** `CharacterJson.CurrentSchemaVersion` goes from 3 to 4. Migration for older documents: nothing to change
  (missing fields take the defaults), but the version bump is required because older apps must refuse documents that
  carry the new fields rather than silently drop them on the next save. Every character the new build saves is
  written as v4, whether or not it uses the new fields (the user's call; see Decisions). A round-trip test covers
  v3 → v4.
- `ResourceFormula.TryResolve(Character c, string formula) : ResolvedRoll?`, where
  `ResolvedRoll(IReadOnlyList<DiceTerm> Dice, int Flat, string Description)`:
  - Terms are joined by `+` or `-`. A term is `NdM` (as in `DamageText`), an integer, `level` (total level),
    `<class> level` (a class on the character, matched like class names elsewhere, ignoring case), `proficiency`, or
    an ability: `STR`, `DEX`, `CON`, `INT`, `WIS`, `CHA`, or the full names, ignoring case (the ability modifier).
  - A class the character doesn't have counts as 0, with "(no <class> levels)" in the description.
  - Anything else gives null.
  - `Description` is the human form, e.g. `1d10 + 11 (fighter level)`.
  - Uses `CharacterRules` (`TotalLevel`, `ProficiencyBonus`, `AbilityModifier`) and existing class-name matching.
- Applying the result: a domain method `Character.UseResourceWithRoll(Resource r, int total) : string`. It spends one
  use (returns "no uses left" without applying if empty), applies Heal or temporary HP per `r.Result`, and returns the
  log text. View models call it and don't mutate state themselves (project rule).

### Presentation

- `ResourceUseViewModel(Character c, Resource r, ResolvedRoll roll, Random random)`, reusing `DamageBox` (one box per
  dice group: group the terms by die size, the same as the attack roller's boxes) and `AttackMath.RollDice` and
  `Range`. It exposes `Name`, `FormulaText`, `Boxes`, `RollForMe`, `Total` (int?, null until every box is valid),
  `ResultText`, and `CanUse`.
- `IDialogs.UseResourceAsync(ResourceUseViewModel vm) : Task<bool>`.
- `PlayViewModel.UseResource` becomes:
  - **no Roll, or no uses left:** today's behaviour
  - **unreadable Roll:** spend and log "couldn't read the roll"
  - **otherwise:** open the dialog; on true, call `UseResourceWithRoll` and log
- A tooltip string per resource: computed in Presentation as a small helper so both apps show the same text.

### UI

- **Details → Resources** (both apps): two new columns, **Roll** (text, with an error line or tooltip when it can't be
  read) and **Result** (dropdown). Accessible names on both.
- **Play → Resources** (both apps): a tooltip on the row (WPF `ToolTip`; web `title` plus an accessible description),
  and the Use button unchanged.
- **Dialogs:** a desktop `ResourceUseDialog` and a web `ResourceUseDialog.razor`, laid out like the attack roller's:

  ```
  Second Wind                                1 / 1 left
  Roll 1d10 + 11 (fighter level)
  1d10   [ 7 ]  (1–10)
  Total 18 → Heal 18
  [Roll for me]                     [Cancel] [Use]
  ```

## Tests

- `ResourceFormula`: dice and numbers; `level`; `fighter level` on a multiclass character; a class the character
  lacks (0); `proficiency`; `CON` and `constitution`; signs; junk → null; the description text.
- Schema: a v3 document loads with defaults, and saving writes v4. A v5 document is refused (existing rule).
- `UseResourceWithRoll`: heal clamps at max HP; temporary HP keeps the higher value; Just show changes no HP; with
  no uses left nothing is applied.
- `ResourceUseViewModel`: boxes and ranges, Roll for me fills empty boxes, `Total` is null until valid, `ResultText`.
- `PlayViewModel.UseResource`: no Roll → old behaviour; unreadable → spends and logs; dialog cancel → nothing spent.

## Settled after the builder check (my calls)

- **Server:** the Supabase `schema_version` column is a plain `int not null` with no upper bound, so v4 rows need no
  migration.
- **`<class> level`** matches class names the way `CharacterRules` already does (trimmed, ignoring case). Several
  entries of the same class add up. A class the character doesn't have counts as 0.
- **Flat-only formulas** (e.g. `WIS`): the dialog still opens, with no dice boxes, and the total is ready at once. A
  total below 0 is applied as 0 and shown as it is.
- **Log text** uses the domain's own messages: `Second Wind: rolled 18. <Heal's returned text>` (or the temporary-HP
  outcome), so HP at max, unconscious and similar cases read the way the rest of Play already reports them.

## Left to the builder

- Column widths and exact wording of labels, tooltips and log lines (within the examples).
- Whether the formula error in Details is an inline line or a tooltip, per app.
