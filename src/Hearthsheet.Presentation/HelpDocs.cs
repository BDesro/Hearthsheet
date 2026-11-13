using System.Text;
using System.Text.RegularExpressions;

namespace Hearthsheet.Presentation;

public enum HelpBlockKind { Heading, Paragraph, Bullet, Image }

public sealed record HelpRun(string Text, bool Bold = false, string? Link = null);

/// <param name="Level">1-3 for headings.</param>
/// <param name="Anchor">The heading's GitHub-style slug.</param>
/// <param name="Image">An image path such as "images/x.png"; its alt text is <c>Runs[0]</c>.</param>
public sealed record HelpBlock(HelpBlockKind Kind, IReadOnlyList<HelpRun> Runs, int Level = 0, string? Anchor = null, string? Image = null);

/// <summary>Where a help link goes: an external <paramref name="Url"/>, or another doc and/or a heading in it.</summary>
public sealed record HelpLink(string? Url, string? Doc, string? Anchor);

/// <summary>
/// The player docs in <c>docs/user/</c>, parsed from a deliberately small Markdown subset (headings, paragraphs, "-"
/// bullets, bold, links, images on their own line) so each UI can draw them without a Markdown library. Anything
/// outside the subset comes through as plain text.
/// </summary>
public static partial class HelpDocs
{
    public const string WikidotUrl = "https://dnd5e.wikidot.com";

    public const string Tutorial = "tutorial.md", BeginnersGuide = "beginners-guide.md", Legal = "legal.md";

    /// <summary>The tutorial heading the New character dialog's "Need help?" opens at.</summary>
    public const string CreatingACharacter = "creating-a-character";

    public sealed record HelpTopic(string File, string Title);

    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(Tutorial, "Tutorial"),
        new(BeginnersGuide, "Beginner's guide to 5e"),
        new(Legal, "Legal and credits"),
    ];

    /// <summary>A bundled doc, parsed. Throws if <paramref name="file"/> isn't bundled.</summary>
    public static IReadOnlyList<HelpBlock> Load(string file)
    {
        using var stream = Open(file) ?? throw new FileNotFoundException($"Help doc '{file}' is not bundled.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>A bundled image's bytes, such as "images/sheet.png", or null.</summary>
    public static byte[]? LoadImage(string path)
    {
        using var stream = Open(path);
        if (stream is null) return null;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    // The csproj names resources "help/<path>", with the build machine's separator inside the path.
    private static Stream? Open(string path)
    {
        var assembly = typeof(HelpDocs).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.Replace('\\', '/').Equals("help/" + path, StringComparison.OrdinalIgnoreCase));
        return name is null ? null : assembly.GetManifestResourceStream(name);
    }

    [GeneratedRegex(@"^(#{1,3})\s+(.+)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)$")] private static partial Regex ImageLine();

    public static IReadOnlyList<HelpBlock> Parse(string markdown)
    {
        var blocks = new List<HelpBlock>();
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, Inline(string.Join(' ', paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (HeadingLine().Match(line) is { Success: true } h)
            {
                Flush();
                var text = h.Groups[2].Value.Trim();
                blocks.Add(new HelpBlock(HelpBlockKind.Heading, Inline(text), h.Groups[1].Length, Slug(text)));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                blocks.Add(new HelpBlock(HelpBlockKind.Bullet, Inline(line[2..].Trim())));
            }
            else if (ImageLine().Match(line) is { Success: true } img)
            {
                Flush();
                blocks.Add(new HelpBlock(HelpBlockKind.Image, [new HelpRun(img.Groups[1].Value)], Image: img.Groups[2].Value));
            }
            else paragraph.Add(line);
        }
        Flush();
        return blocks;
    }

    /// <summary>Splits text into plain, <c>**bold**</c> and <c>[text](url)</c> runs; unmatched markers stay literal.</summary>
    private static List<HelpRun> Inline(string text)
    {
        var runs = new List<HelpRun>();
        var plain = new StringBuilder();
        void FlushPlain()
        {
            if (plain.Length > 0) runs.Add(new HelpRun(plain.ToString()));
            plain.Clear();
        }

        for (var i = 0; i < text.Length;)
        {
            if (text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var end and > 0)
            {
                FlushPlain();
                runs.Add(new HelpRun(text[(i + 2)..end], Bold: true));
                i = end + 2;
            }
            else if (text[i] == '[' && text.IndexOf("](", i, StringComparison.Ordinal) is var mid and > 0
                     && text.IndexOf(')', mid) is var close and > 0)
            {
                FlushPlain();
                runs.Add(new HelpRun(text[(i + 1)..mid], Link: text[(mid + 2)..close]));
                i = close + 1;
            }
            else plain.Append(text[i++]);
        }
        FlushPlain();
        return runs;
    }

    /// <summary>GitHub's heading anchor: lowercase, spaces to "-", anything but letters, digits, "-" and "_" dropped.</summary>
    public static string Slug(string heading)
    {
        var slug = new StringBuilder();
        foreach (var c in heading.Trim().ToLowerInvariant())
        {
            if (c == ' ') slug.Append('-');
            else if (char.IsLetterOrDigit(c) || c is '-' or '_') slug.Append(c);
        }
        return slug.ToString();
    }

    /// <returns>The index of the heading with that anchor, or -1.</returns>
    public static int FindAnchor(IReadOnlyList<HelpBlock> blocks, string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return -1;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i].Kind == HelpBlockKind.Heading && blocks[i].Anchor == anchor) return i;
        return -1;
    }

    /// <returns>Where the link goes, or null for anything that isn't http(s), a .md doc or a #heading.</returns>
    public static HelpLink? ResolveLink(string target)
    {
        if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return new HelpLink(target, null, null);
        if (target.StartsWith('#')) return target.Length > 1 ? new HelpLink(null, null, target[1..]) : null;
        if (target.Contains(':')) return null;
        var hash = target.IndexOf('#');
        var doc = hash < 0 ? target : target[..hash];
        if (!doc.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;
        return new HelpLink(null, doc, hash < 0 || hash == target.Length - 1 ? null : target[(hash + 1)..]);
    }
}
