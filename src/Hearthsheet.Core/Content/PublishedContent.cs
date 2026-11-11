namespace Hearthsheet.Core.Content;

/// <summary>
/// Names of published (non-SRD) 5e options, used only as picker suggestions. Names only: no rules text (see NOTICE.md).
/// Mechanical class facts live in <see cref="SrdCatalog.Classes"/>.
/// </summary>
public static class PublishedContent
{
    /// <summary>Subclass names by class, alphabetical within each class.</summary>
    public static IReadOnlyDictionary<string, string[]> Subclasses { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Artificer"] = ["Alchemist", "Armorer", "Artillerist", "Battle Smith", "Forge Adept", "Mastermaker", "Maverick"],
            ["Barbarian"] =
            [
                "Path of the Ancestral Guardian", "Path of the Battlerager", "Path of the Beast", "Path of the Berserker",
                "Path of the Depths", "Path of the Giant", "Path of the Juggernaut", "Path of the Storm Herald",
                "Path of the Totem Warrior", "Path of Wild Magic", "Path of the Zealot",
            ],
            ["Bard"] =
            [
                "College of Creation", "College of the Dirge Singer", "College of Eloquence", "College of Glamour", "College of Lore",
                "College of the Maestro", "College of Spirits", "College of Swords", "College of Tragedy", "College of Valor",
                "College of Whispers",
            ],
            ["Blood Hunter"] = ["Order of the Ghostslayer", "Order of the Lycan", "Order of the Mutant", "Order of the Profane Soul"],
            ["Cleric"] =
            [
                "Ambition Domain", "Arcana Domain", "Beauty Domain", "Blood Domain", "Death Domain", "Fate Domain", "Forge Domain",
                "Grave Domain", "Knowledge Domain", "Life Domain", "Light Domain", "Mind Domain", "Moon Domain", "Nature Domain",
                "Order Domain", "Peace Domain", "Solidarity Domain", "Strength Domain", "Tempest Domain", "Trickery Domain",
                "Twilight Domain", "War Domain", "Zeal Domain",
            ],
            ["Druid"] =
            [
                "Circle of the Blighted", "Circle of Dreams", "Circle of the Forged", "Circle of the Land", "Circle of the Moon",
                "Circle of the Primeval", "Circle of the Shepherd", "Circle of Spores", "Circle of Stars", "Circle of Wildfire",
            ],
            ["Fighter"] =
            [
                "Arcane Archer", "Banneret", "Battle Master", "Cavalier", "Champion", "Echo Knight", "Eldritch Knight", "Gunslinger",
                "Psi Warrior", "Renegade", "Rune Knight", "Samurai",
            ],
            ["Monk"] =
            [
                "Way of the Ascendant Dragon", "Way of the Astral Self", "Way of the Cobalt Soul", "Way of the Drunken Master",
                "Way of the Four Elements", "Way of the Kensei", "Way of the Living Weapon", "Way of the Long Death", "Way of Mercy",
                "Way of the Open Hand", "Way of Shadow", "Way of the Sun Soul",
            ],
            ["Paladin"] =
            [
                "Oath of the Ancients", "Oath of Conquest", "Oath of the Crown", "Oath of Devotion", "Oath of Glory",
                "Oath of the Open Sea", "Oath of Redemption", "Oath of Vengeance", "Oath of the Watchers", "Oathbreaker",
            ],
            ["Ranger"] =
            [
                "Beast Master", "Drakewarden", "Fey Wanderer", "Gloom Stalker", "Horizon Walker", "Hunter", "Monster Slayer",
                "Swarmkeeper",
            ],
            ["Rogue"] =
            [
                "Arcane Trickster", "Assassin", "Inquisitive", "Mastermind", "Phantom", "Scout", "Soulknife", "Swashbuckler",
                "Thief", "Wild Card",
            ],
            ["Sorcerer"] =
            [
                "Aberrant Mind", "Clockwork Soul", "Divine Soul", "Draconic Bloodline", "Lunar Sorcery", "Pyromancer", "Runechild",
                "Shadow Magic", "Storm Sorcery", "Wild Magic",
            ],
            ["Warlock"] =
            [
                "The Archfey", "The Celestial", "The Fathomless", "The Fiend", "The Genie", "The Great Old One", "The Hexblade",
                "The Kraken", "The Undead", "The Undying",
            ],
            ["Wizard"] =
            [
                "Blood Magic", "Bladesinging", "Chronurgy Magic", "Graviturgy Magic", "Order of Scribes", "School of Abjuration",
                "School of Conjuration", "School of Divination", "School of Enchantment", "School of Evocation", "School of Illusion",
                "School of Necromancy", "School of Transmutation", "War Magic",
            ],
        };

    public static IReadOnlyList<string> SubclassesFor(string className) =>
        Subclasses.TryGetValue(className.Trim(), out var list) ? list : [];

    /// <summary>Species beyond the SRD nine.</summary>
    public static IReadOnlyList<string> Species { get; } =
    [
        "Aarakocra", "Aasimar", "Aetherborn", "Astral Elf", "Autognome", "Aven", "Bugbear", "Centaur", "Changeling",
        "Custom Lineage", "Deep Gnome", "Dhampir", "Duergar", "Eladrin", "Fairy", "Firbolg", "Genasi (Air)", "Genasi (Earth)",
        "Genasi (Fire)", "Genasi (Water)", "Giff", "Githyanki", "Githzerai", "Goblin", "Goliath", "Grung", "Hadozee",
        "Harengon", "Hexblood", "Hobgoblin", "Kalashtar", "Kender", "Kenku", "Khenra", "Kobold", "Kor", "Leonin",
        "Lizardfolk", "Locathah", "Loxodon", "Merfolk", "Minotaur", "Naga", "Orc", "Owlin", "Plasmoid", "Reborn", "Satyr",
        "Sea Elf", "Shadar-kai", "Shifter", "Simic Hybrid", "Siren", "Tabaxi", "Thri-kreen", "Tortle", "Triton", "Vampire",
        "Vedalken", "Verdan", "Warforged", "Yuan-ti",
    ];

    public static IReadOnlyList<string> Backgrounds { get; } =
    [
        "Acolyte", "Anthropologist", "Archaeologist", "Ashari", "Astral Drifter", "Athlete", "Azorius Functionary",
        "Black Fist Double Agent", "Boros Legionnaire", "Caravan Specialist", "Celebrity Adventurer's Scion", "Charlatan",
        "City Watch", "Clan Crafter", "Cloistered Scholar", "Cormanthor Refugee", "Courtier", "Criminal", "Dimir Operative",
        "Dissenter", "Dragon Casualty", "Earthspur Miner", "Entertainer", "Faceless", "Faction Agent", "Failed Merchant",
        "Far Traveler", "Feylost", "Fisher", "Folk Hero", "Gambler", "Gate Urchin", "Gate Warden", "Giant Foundling",
        "Gladiator", "Golgari Agent", "Grinner", "Gruul Anarch", "Guild Artisan", "Guild Merchant", "Harborfolk",
        "Haunted One", "Hermit", "Hillsfar Merchant", "Hillsfar Smuggler", "House Agent", "Inheritor", "Initiate",
        "Inquisitor", "Investigator", "Iron Route Bandit", "Izzet Engineer", "Knight", "Knight of Solamnia",
        "Knight of the Order", "Lorehold Student", "Mage of High Sorcery", "Marine", "Mercenary Veteran", "Mulmaster Aristocrat",
        "Noble", "Orzhov Representative", "Outlander", "Phlan Insurgent", "Phlan Refugee", "Pirate", "Plaintiff",
        "Planar Philosopher", "Prismari Student", "Quandrix Student", "Rakdos Cultist", "Rewarded", "Rival Intern", "Ruined",
        "Rune Carver", "Sage", "Sailor", "Secret Identity", "Selesnya Initiate", "Shade Fanatic", "Shipwright",
        "Silverquill Student", "Simic Scientist", "Smuggler", "Soldier", "Spy", "Stojanow Prisoner", "Ticklebelly Nomad",
        "Trade Sheriff", "Urban Bounty Hunter", "Urchin", "Uthgardt Tribe Member", "Vizier", "Volstrucker Agent",
        "Waterdhavian Noble", "Wildspacer", "Witchlight Hand", "Witherbloom Student",
    ];
}
