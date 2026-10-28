using System.IO.Compression;
using System.Text;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Core.Application;

public sealed record CharacterSummary(Guid Id, string Name, string Description, DateTime UpdatedUtc);

/// <summary>Storage for characters. Implementations must make each Save atomic.</summary>
public interface ICharacterRepository
{
    IReadOnlyList<CharacterSummary> List();
    /// <exception cref="CharacterFormatException">Stored data could not be read or migrated.</exception>
    Character? Load(Guid id);
    void Save(Character character);
    bool Delete(Guid id);
}

/// <summary>Use cases that create characters or move them in and out of files (create, duplicate, import/export).</summary>
public sealed class CharacterLibrary(
    ICharacterRepository repository, PortableCharacterFile portable, string appVersion, ILogger<CharacterLibrary> logger)
{
    public Character Create(NewCharacterOptions options)
    {
        var c = CharacterFactory.Create(options);
        repository.Save(c);
        logger.LogInformation("Character created: {Name} ({ClassAndLevel})", c.Identity.Name, CharacterRules.ClassAndLevelText(c));
        return c;
    }

    public Character Duplicate(Character source)
    {
        var copy = CharacterJson.Clone(source);
        copy.Id = Guid.NewGuid();
        copy.Identity.Name = source.Identity.Name + " (copy)";
        repository.Save(copy);
        logger.LogInformation("Character duplicated: {Name}", copy.Identity.Name);
        return copy;
    }

    public void Export(Character c, string path)
    {
        portable.ExportToFile(c, appVersion, path);
        logger.LogInformation("Character exported: {Name} -> {File}", c.Identity.Name, Path.GetFileName(path));
    }

    /// <exception cref="CharacterFormatException">The file is invalid, corrupt or from a newer version.</exception>
    public Character Import(string path)
    {
        var c = portable.ImportFromFile(path);
        repository.Save(c);
        logger.LogInformation("Character imported: {Name} from {File}", c.Identity.Name, Path.GetFileName(path));
        return c;
    }

    public string ExportText(Character c) => portable.Export(c, appVersion);

    /// <exception cref="CharacterFormatException">The text is invalid, corrupt, too large or from a newer version.</exception>
    public Character Import(string text, string sourceName)
    {
        var c = portable.Import(text);
        repository.Save(c);
        logger.LogInformation("Character imported: {Name} from {File}", c.Identity.Name, sourceName);
        return c;
    }

    /// <summary>Writes every stored character as a .dndchar entry in a zip. Unreadable characters are skipped and logged.</summary>
    public void ExportAll(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in repository.List())
        {
            Character? c;
            try { c = repository.Load(summary.Id); }
            catch (CharacterFormatException ex)
            {
                logger.LogWarning(ex, "Export all: skipped unreadable character {Id}", summary.Id);
                continue;
            }
            if (c is null) continue;

            var stem = SafeFileName(c.Identity.Name);
            var name = stem;
            for (var n = 2; !used.Add(name); n++) name = $"{stem} ({n})";
            var entry = archive.CreateEntry(name + PortableCharacterFile.FileExtension);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(portable.Export(c, appVersion));
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch)).Trim();
        return s.Length == 0 ? "Character" : s;
    }
}
