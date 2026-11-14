# Intellectual property notes

Hearthsheet is an unofficial fan tool. It is not affiliated with, endorsed or sponsored by Wizards of the Coast. *Dungeons & Dragons* and *D&D* are trademarks of Wizards of the Coast LLC. Hearthsheet is also not affiliated with the D&D 5e wikidot (<https://dnd5e.wikidot.com>); the app and its docs only link to it as a reference and contain none of its content.

The player-facing version of these notes is [docs/user/legal.md](docs/user/legal.md), shown in the app under **Help → Legal and credits**.

## What the app contains

- **Mechanical facts from the System Reference Document 5.1 (SRD 5.1).** These are class names, hit dice, saving-throw proficiencies, spellcasting abilities, spell-slot progression tables, the standard skills and conditions, species and alignment names, and weapon names (`src/Hearthsheet.Core/Content/SrdCatalog.cs`, `Domain/Enums.cs`).
  This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by Wizards of the Coast LLC, available at <https://dnd.wizards.com/resources/systems-reference-document>. The SRD 5.1 is licensed under the Creative Commons Attribution 4.0 International License, available at <https://creativecommons.org/licenses/by/4.0/legalcode>.
- **Published (non-SRD) names and class facts.** `src/Hearthsheet.Core/Content/PublishedContent.cs` lists subclass, species and background names from the published books, setting books and a few Wizards-affiliated homebrew releases, used only as picker suggestions. The Artificer's hit die, saving throws, spellcasting type and multiclass facts are in `SrdCatalog.cs`. Only names and game-mechanical facts are included; no descriptions or rules text are reproduced.
- **Kamen Rider.** A fan-made class from D&D Wiki (<https://dandwiki.com/wiki/Kamen_Rider_(5e_Class)>), based on the Kamen Rider franchise. Hearthsheet includes only its name, hit die, saving-throw proficiencies and archetype names. No text from the class is reproduced. Hearthsheet is not affiliated with or endorsed by D&D Wiki, Ishimori Productions or Toei Company.
- **Blood Hunter.** Blood Hunter is a class created by Matt Mercer for Critical Role. Hearthsheet includes only its name, hit die, saving-throw proficiencies, multiclass prerequisites and multiclass proficiencies. No text from the class is reproduced. Hearthsheet is not affiliated with or endorsed by Critical Role.
- **Rules computed in code:** modifiers, proficiency bonus, death saves, rests. The rules themselves (as opposed to their expression in text) are implemented from their mechanics, and no rules text is reproduced.

## What it deliberately does not contain

- **No rulebook text.** It has no spell, feat, class-feature or monster descriptions. Spell and feature description fields are empty until the player types in their own notes. The UI's short helper phrases are original wording.
- **The app icon is original artwork** (a red d20 on black, `src/Hearthsheet.App/Assets/Hearthsheet.ico`). The official D&D ampersand logo is a Wizards of the Coast trademark and is intentionally not used.
- **No official artwork, logos, fonts or character-sheet PDF.** The sheet view is an original layout that follows the *organization* of the familiar 5e sheet (where boxes are and what they hold), drawn with standard WPF controls. It does not trace or embed the official PDF.

## If content is added later

- Content from SRD 5.1 or SRD 5.2 (both CC-BY-4.0) can be bundled with the attribution above (SRD 5.2 needs its own attribution line).
- Non-SRD content may be bundled as names and mechanical facts only (as above). Never copy descriptions or rules text from books or sites; feature, spell and item text stays the player's own.
- Keep bundled content in data files separate from code, so its licensing is clear and it can be swapped out.
