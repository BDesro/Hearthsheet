using System.Text.Json.Nodes;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("hearthsheet-tests-").FullName;
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public static class Samples
{
    public static Character Rich()
    {
        var c = CharacterFactory.Create(new NewCharacterOptions("Arannis", "Wizard", 5, "Elf", "Sage", "Beau"));
        c.Abilities.Intelligence = 18;
        c.SkillEntry(Skill.Arcana).Proficiency = ProficiencyLevel.Expertise;
        c.Items.Add(new Item { Name = "Staff", Weight = 4, Equipped = true, Modifiers = { new Modifier { Target = "ac", Value = 1 } } });
        c.Features.Add(new Feature { Name = "Arcane Recovery", Source = FeatureSource.Class, Description = "Recover slots." });
        c.Spellcasting.Spells.Add(new Spell { Name = "Shield", Level = 1, Prepared = true });
        c.Effects.Add(new Effect { Name = "Bless", Expiry = EffectExpiry.ShortRest });
        c.Conditions.Add(Condition.Prone);
        c.Proficiencies.Add(new Proficiency { Category = ProficiencyCategory.Language, Name = "Elvish" });
        c.Resources.Add(new Resource { Name = "Custom", Maximum = 3, Current = 2, RecoveryAmount = 1 });
        c.Currency.Gold = 42;
        c.Combat.TemporaryHp = 3;
        return c;
    }
}

public class SerializationTests
{
    private readonly PortableCharacterFile _portable = new();

    [Fact]
    public void RoundTrip_PreservesState()
    {
        var original = Samples.Rich();
        var copy = CharacterJson.Deserialize(CharacterJson.Serialize(original), CharacterJson.CurrentSchemaVersion);
        Assert.Equal(CharacterJson.Serialize(original), CharacterJson.Serialize(copy));
        Assert.Equal(original.Id, copy.Id);
    }

    [Fact]
    public void SchemaOneCharacter_KeepsItsEntriesAsTheyWere()
    {
        // Shape written by 1.2.x: no weapons, wondrous items or feature modifiers.
        const string v1 = """
            {"abilities":{"strength":16},
             "attacks":[{"name":"Longsword","ability":"Strength","proficient":true,"bonus":1,"damage":"1d8+4 slashing","notes":"silvered"}],
             "items":[{"name":"Cloak of Protection","equipped":true,"modifiers":[{"target":"ac","kind":"Bonus","value":1}]}],
             "features":[{"name":"Dueling","source":"Class","description":"+2 damage one-handed"}]}
            """;
        var c = CharacterJson.Deserialize(v1, 1);

        var attack = Assert.Single(c.Attacks);
        Assert.Null(attack.WeaponId);
        Assert.Equal("1d8+4 slashing", CharacterRules.DamageRoll(c, attack));
        Assert.Equal(6, CharacterRules.AttackBonus(c, attack));
        Assert.Equal("Cloak of Protection", Assert.Single(c.Items).Name);
        Assert.Equal(11, CharacterRules.ArmorClass(c));
        Assert.Equal("+2 damage one-handed", Assert.Single(c.Features).Description);
        Assert.Empty(c.Weapons);
        Assert.Empty(c.WondrousItems);
    }

    [Fact]
    public void ExportImport_ProducesEquivalentCharacterWithNewId()
    {
        var original = Samples.Rich();
        var imported = _portable.Import(_portable.Export(original, "1.0.0"));
        Assert.NotEqual(original.Id, imported.Id);
        imported.Id = original.Id;
        Assert.Equal(CharacterJson.Serialize(original), CharacterJson.Serialize(imported));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"something-else\",\"schemaVersion\":1,\"character\":{}}")]
    [InlineData("{\"format\":1,\"schemaVersion\":1,\"character\":{}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":1}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":0,\"character\":{}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":1,\"character\":{\"abilities\":{\"strength\":\"lots\"}}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":1,\"character\":{\"conditions\":[\"OnFire\"]}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":2,\"character\":{\"identity\":{\"level\":\"lots\"}}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":2,\"character\":{\"identity\":[]}}")]
    [InlineData("{\"format\":\"dndsheet.character\",\"schemaVersion\":2,\"character\":{\"spellcasting\":{\"ability\":5}}}")]
    public void Import_RejectsInvalidFiles(string text) =>
        Assert.Throws<CharacterFormatException>(() => _portable.Import(text));

    [Fact]
    public void SchemaTwo_MigratesSingleClassToOneEntry()
    {
        const string v2 = """{"identity":{"className":"Fighter","subclass":"Echo Knight","level":11},"combat":{"hitDieSize":10,"hitDiceRemaining":7},"spellcasting":{"ability":null}}""";
        var c = CharacterJson.Deserialize(v2, 2);
        var e = Assert.Single(c.Identity.Classes);
        Assert.Equal(("Fighter", "Echo Knight", 11, 10, 7, CasterType.None), (e.Name, e.Subclass, e.Level, e.HitDie, e.HitDiceRemaining, e.Caster));
    }

    [Fact]
    public void SchemaThree_KeyOrderDoesNotTriggerSubclassRule()
    {
        const string v3 = """{"identity":{"classes":[{"caster":"None","subclass":"Eldritch Knight","name":"Fighter","level":7}]}}""";
        Assert.Equal(CasterType.None, Assert.Single(CharacterJson.Deserialize(v3, 3).Identity.Classes).Caster);
    }

    [Fact]
    public void SchemaTwo_EldritchKnight_MigratesToThirdCaster()
    {
        const string v2 = """{"identity":{"className":"Fighter","subclass":"Eldritch Knight","level":7}}""";
        var e = Assert.Single(CharacterJson.Deserialize(v2, 2).Identity.Classes);
        Assert.Equal((CasterType.Third, Ability.Intelligence), (e.Caster, e.SpellcastingAbility!.Value));
    }

    [Fact]
    public void SchemaTwo_MissingHitDiceRemaining_FallsBackToLevel()
    {
        const string v2 = """{"identity":{"className":"Fighter","level":5}}""";
        Assert.Equal(5, Assert.Single(CharacterJson.Deserialize(v2, 2).Identity.Classes).HitDiceRemaining);
    }

    [Fact]
    public void Import_SchemaTwoEnvelope_MigratesToOneEntry()
    {
        const string text = """{"format":"dndsheet.character","schemaVersion":2,"character":{"identity":{"className":"Fighter","level":11}}}""";
        var e = Assert.Single(_portable.Import(text).Identity.Classes);
        Assert.Equal(("Fighter", 11), (e.Name, e.Level));
    }

    [Fact]
    public void SchemaTwo_UnknownClassWithAbility_BecomesFullCaster()
    {
        const string v2 = """{"identity":{"className":"Witch","level":3},"spellcasting":{"ability":"Intelligence"}}""";
        var e = Assert.Single(CharacterJson.Deserialize(v2, 2).Identity.Classes);
        Assert.Equal(CasterType.Full, e.Caster);
        Assert.Equal(Ability.Intelligence, e.SpellcastingAbility);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SchemaOneOrTwo_EmptyDocument_LoadsOneBlankLevelOneEntry(int version)
    {
        var e = Assert.Single(CharacterJson.Deserialize("{}", version).Identity.Classes);
        Assert.Equal(("", 1, 1), (e.Name, e.Level, e.HitDiceRemaining));
    }

    [Fact]
    public void SchemaThree_RoundTripsClasses()
    {
        var c = new Character();
        c.Identity.Classes[0] = new ClassLevel { Name = "Fighter", Subclass = "Champion", Level = 5, HitDie = 10, HitDiceRemaining = 3, Caster = CasterType.None };
        c.Identity.Classes.Add(new ClassLevel { Name = "Wizard", Level = 2, HitDie = 6, HitDiceRemaining = 2, Caster = CasterType.Full, SpellcastingAbility = Ability.Intelligence });
        var back = CharacterJson.Deserialize(CharacterJson.Serialize(c), 3).Identity.Classes;
        Assert.Equal(2, back.Count);
        Assert.Equal(("Fighter", "Champion", 5, 10, 3), (back[0].Name, back[0].Subclass, back[0].Level, back[0].HitDie, back[0].HitDiceRemaining));
        Assert.Equal(("Wizard", CasterType.Full, Ability.Intelligence), (back[1].Name, back[1].Caster, back[1].SpellcastingAbility));
    }

    [Fact]
    public void Normalize_RepairsClasses()
    {
        var empty = CharacterJson.Deserialize("""{"identity":{"classes":[]}}""", 3);
        Assert.Single(empty.Identity.Classes);

        var over = CharacterJson.Deserialize(
            """{"identity":{"classes":[{"name":"Fighter","level":15},{"name":"Wizard","level":10,"hitDiceRemaining":10}]}}""", 3);
        Assert.Equal([15, 5], over.Identity.Classes.Select(x => x.Level));
        Assert.Equal(5, over.Identity.Classes[1].HitDiceRemaining);

        var dice = CharacterJson.Deserialize("""{"identity":{"classes":[{"level":3,"hitDiceRemaining":9}]}}""", 3);
        Assert.Equal(3, dice.Identity.Classes[0].HitDiceRemaining);
    }

    [Fact]
    public void Import_RejectsNewerSchema()
    {
        var text = _portable.Export(new Character(), "9.0.0").Replace($"\"schemaVersion\": {CharacterJson.CurrentSchemaVersion}", "\"schemaVersion\": 99");
        var ex = Assert.Throws<CharacterFormatException>(() => _portable.Import(text));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void Import_RejectsExcessiveNesting()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "1" + new string('}', 100);
        Assert.Throws<CharacterFormatException>(() => _portable.Import(deep));
    }

    [Fact]
    public void Import_ClampsValuesAndRepairsStructure()
    {
        const string text = """
            {"format":"dndsheet.character","schemaVersion":1,"character":{
              "abilities":{"strength":400},
              "skills":[{"skill":"Stealth","proficiency":"Expertise"},{"skill":"Stealth"}],
              "savingThrows":[]
            }}
            """;
        var c = _portable.Import(text);
        Assert.Equal(30, c.Abilities.Strength);
        Assert.Equal(18, c.Skills.Count);
        Assert.Equal(ProficiencyLevel.Expertise, c.SkillEntry(Skill.Stealth).Proficiency);
        Assert.Equal(6, c.SavingThrows.Count);
        Assert.Equal(9, c.Spellcasting.Slots.Count);
    }
}

public class RepositoryTests : IDisposable
{
    private readonly TempDir _dir = new();
    private SqliteCharacterRepository NewRepository() =>
        new(_dir.File("characters.db"), NullLogger<SqliteCharacterRepository>.Instance);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SaveLoadUpdateDelete()
    {
        var repo = NewRepository();
        var c = Samples.Rich();
        repo.Save(c);

        var loaded = repo.Load(c.Id)!;
        Assert.Equal(CharacterJson.Serialize(c), CharacterJson.Serialize(loaded));

        loaded.Identity.Name = "Renamed";
        loaded.TakeDamage(5);
        repo.Save(loaded);
        var summaries = repo.List();
        Assert.Single(summaries);
        Assert.Equal("Renamed", summaries[0].Name);
        Assert.Contains("Wizard 5", summaries[0].Description);
        Assert.Equal(loaded.Combat.CurrentHp, repo.Load(c.Id)!.Combat.CurrentHp);

        Assert.True(repo.Delete(c.Id));
        Assert.Null(repo.Load(c.Id));
        Assert.False(repo.Delete(c.Id));
    }

    [Fact]
    public void Data_SurvivesReopeningDatabase()
    {
        var c = Samples.Rich();
        NewRepository().Save(c);
        var reopened = NewRepository();
        Assert.Equal("Arannis", reopened.Load(c.Id)!.Identity.Name);
    }

    [Fact]
    public void MultipleCharacters_AreListedByName()
    {
        var repo = NewRepository();
        foreach (var name in new[] { "zed", "Alice", "bob" })
        {
            var c = new Character();
            c.Identity.Name = name;
            repo.Save(c);
        }
        Assert.Equal(["Alice", "bob", "zed"], repo.List().Select(s => s.Name));
    }

    [Fact]
    public void Load_RowFromNewerSchema_ThrowsFormatException()
    {
        var repo = NewRepository();
        var c = new Character();
        repo.Save(c);
        ExecuteSql($"UPDATE characters SET schema_version = {CharacterJson.CurrentSchemaVersion + 1}");

        Assert.Throws<CharacterFormatException>(() => repo.Load(c.Id));
    }

    [Fact]
    public void Load_CorruptRow_ThrowsFormatException_WithoutBreakingList()
    {
        var repo = NewRepository();
        var good = new Character();
        var bad = new Character();
        repo.Save(good);
        repo.Save(bad);
        ExecuteSql($"UPDATE characters SET data = '{{broken' WHERE id = '{bad.Id}'");

        Assert.Throws<CharacterFormatException>(() => repo.Load(bad.Id));
        Assert.NotNull(repo.Load(good.Id));
        Assert.Equal(2, repo.List().Count);
    }

    [Fact]
    public void Backup_ProducesLoadableCopy()
    {
        var repo = NewRepository();
        var c = Samples.Rich();
        repo.Save(c);
        var backupPath = _dir.File("backup.db");
        repo.BackupTo(backupPath);

        var restored = new SqliteCharacterRepository(backupPath, NullLogger<SqliteCharacterRepository>.Instance);
        Assert.Equal("Arannis", restored.Load(c.Id)!.Identity.Name);
    }

    [Fact]
    public void Library_DuplicateCreatesIndependentCopy()
    {
        var repo = NewRepository();
        var library = new CharacterLibrary(repo, new PortableCharacterFile(),
            "1.0.0", NullLogger<CharacterLibrary>.Instance);
        var original = library.Create(new NewCharacterOptions("Hero", "Fighter", 3, "Human", "Soldier"));
        var copy = library.Duplicate(original);

        copy.Abilities.Strength = 20;
        repo.Save(copy);
        Assert.Equal(2, repo.List().Count);
        Assert.Equal(10, repo.Load(original.Id)!.Abilities.Strength);
        Assert.Equal("Hero (copy)", repo.Load(copy.Id)!.Identity.Name);

        var exportPath = _dir.File("hero.dndchar");
        library.Export(original, exportPath);
        var imported = library.Import(exportPath);
        Assert.Equal(3, repo.List().Count);
        Assert.Equal("Hero", imported.Identity.Name);
    }

    private CharacterLibrary NewLibrary(SqliteCharacterRepository repo) =>
        new(repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);

    [Fact]
    public void ImportText_RejectsOversizedText()
    {
        var library = NewLibrary(NewRepository());
        var text = new string('x', (int)PortableCharacterFile.MaxFileBytes + 1);
        Assert.Throws<CharacterFormatException>(() => library.Import(text, "big.dndchar"));
    }

    [Fact]
    public void ExportAll_WritesOneEntryPerCharacter_AndRoundTrips()
    {
        var repo = NewRepository();
        var library = NewLibrary(repo);
        foreach (var name in new[] { "Arannis", "Arannis", "Bo/rin" })
        {
            var c = new Character();
            c.Identity.Name = name;
            repo.Save(c);
        }

        using var ms = new MemoryStream();
        library.ExportAll(ms);
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);

        var names = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(["Arannis (2).dndchar", "Arannis.dndchar", "Bo_rin.dndchar"], names);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var imported = library.Import(reader.ReadToEnd(), entry.FullName);
            Assert.Equal(entry.FullName.StartsWith("Arannis") ? "Arannis" : "Bo/rin", imported.Identity.Name);
        }
    }

    private void ExecuteSql(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dir.File("characters.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

public class ChangeTrackerTests
{
    [Fact]
    public void Session_ReportsChangesAnywhereInTheGraph()
    {
        var c = new Character();
        using var session = new CharacterSession(c);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        Assert.False(session.IsDirty);

        c.Abilities.Strength = 14;
        c.SkillEntry(Skill.Stealth).Bonus = 2;
        var item = new Item { Name = "Rope" };
        c.Items.Add(item);
        item.Quantity = 2;
        item.Modifiers.Add(new Modifier());
        c.Spellcasting.SlotLevel(3).Expended = 0; // no-op: unchanged value
        c.Conditions.Add(Condition.Prone);

        Assert.Equal(6, changes);
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Session_StopsTrackingRemovedItems()
    {
        var c = new Character();
        var item = new Item();
        c.Items.Add(item);
        using var session = new CharacterSession(c);
        var changes = 0;
        session.Changed += (_, _) => changes++;

        c.Items.Remove(item);
        item.Name = "gone";
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SchemaTwo_PactResourceBecomesPactSlots()
    {
        const string v2 = """{"identity":{"className":"Warlock","level":5},"resources":[{"name":"Rage","maximum":3,"current":3},{"name":"Pact Magic slots (level 3)","maximum":2,"current":1,"recovery":"ShortRest"}]}""";
        var c = CharacterJson.Deserialize(v2, 2);
        Assert.Equal((3, 2, 1), (c.Spellcasting.PactSlots.Level, c.Spellcasting.PactSlots.Maximum, c.Spellcasting.PactSlots.Expended));
        Assert.Equal("Rage", Assert.Single(c.Resources).Name);
    }

    [Fact]
    public void SchemaTwo_SpellcastingClassLabelKept()
    {
        const string v2 = """{"identity":{"className":"Wizard","level":3},"spellcasting":{"ability":"Intelligence","spellcastingClass":"Wizard"}}""";
        Assert.Equal("Wizard", CharacterJson.Deserialize(v2, 2).Spellcasting.SpellcastingClass);
    }
}

public class UpgradeBackupTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string Db => _dir.File("characters.db");
    private string Backups => _dir.File("backups");

    public UpgradeBackupTests() => Directory.CreateDirectory(Backups);

    public void Dispose() => _dir.Dispose();

    private SqliteCharacterRepository Open(string? backups) =>
        new(Db, NullLogger<SqliteCharacterRepository>.Instance, backups);

    /// <summary>Builds a database exactly as version 1 of the app left it, holding one character.</summary>
    internal static void CreateVersion1Database(string path, Character c)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO meta VALUES ('db_version', '1');
            CREATE TABLE characters (id TEXT PRIMARY KEY, name TEXT NOT NULL, description TEXT NOT NULL,
                schema_version INTEGER NOT NULL, data TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
            INSERT INTO characters VALUES ($id, 'Arannis', '', 1, $data, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');
            """;
        command.Parameters.AddWithValue("$id", c.Id.ToString());
        command.Parameters.AddWithValue("$data", CharacterJson.Serialize(c));
        command.ExecuteNonQuery();
    }

    internal static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void UpgradeFromVersion1_WritesBackupFirst()
    {
        var c = Samples.Rich();
        CreateVersion1Database(Db, c);

        var repo = Open(Backups);

        var backup = Assert.Single(Directory.GetFiles(Backups, "pre-upgrade-db1-*.db"));
        Assert.Equal("1", Scalar(backup, "SELECT value FROM meta WHERE key = 'db_version'"));
        Assert.Equal(1L, Scalar(backup, "SELECT COUNT(*) FROM characters"));
        Assert.Equal("Arannis", repo.Load(c.Id)!.Identity.Name);
    }

    [Fact]
    public void BackupFailure_LeavesDatabaseAtVersion1()
    {
        CreateVersion1Database(Db, Samples.Rich());
        var blocker = _dir.File("blocker");
        File.WriteAllText(blocker, "");

        Assert.Throws<InvalidOperationException>(() => Open(Path.Combine(blocker, "sub")));

        Assert.Equal("1", Scalar(Db, "SELECT value FROM meta WHERE key = 'db_version'"));
        Assert.Equal(0L, Scalar(Db, "SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name = 'dirty'"));
    }

    [Fact]
    public void NoUpgradeNeeded_WritesNoBackup()
    {
        Open(Backups);
        Open(Backups);
        Assert.Empty(Directory.GetFiles(Backups, "pre-upgrade-*"));
    }

    [Fact]
    public void SchemaVersionRaise_BacksUpOnce()
    {
        Open(Backups);
        var previous = CharacterJson.CurrentSchemaVersion - 1;
        Scalar(Db, $"UPDATE meta SET value = '{previous}' WHERE key = 'last_schema_version'");

        Open(Backups);
        Open(Backups);

        Assert.Single(Directory.GetFiles(Backups, $"pre-upgrade-db2-schema{previous}-*.db"));
    }

    [Fact]
    public void NewerDatabase_IsRefusedWithoutBackup()
    {
        CreateVersion1Database(Db, Samples.Rich());
        Scalar(Db, "UPDATE meta SET value = '99' WHERE key = 'db_version'");

        Assert.Throws<InvalidOperationException>(() => Open(Backups));
        Assert.Empty(Directory.GetFiles(Backups));
    }
}
