using Hearthsheet.Presentation;

namespace Hearthsheet.Tests;

public class HelpDocsTests
{
    private static string Text(HelpBlock b) => string.Concat(b.Runs.Select(r => r.Text));

    [Fact]
    public void Parse_headings_paragraphs_bullets()
    {
        var blocks = HelpDocs.Parse("# Title\n\nOne\ntwo\n\n- a\n- b");

        Assert.Equal(4, blocks.Count);
        Assert.Equal(HelpBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("Title", Text(blocks[0]));
        Assert.Equal("title", blocks[0].Anchor);
        Assert.Equal(HelpBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal("One two", Text(blocks[1]));
        Assert.Equal(HelpBlockKind.Bullet, blocks[2].Kind);
        Assert.Equal("a", Text(blocks[2]));
        Assert.Equal(HelpBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal("b", Text(blocks[3]));
    }

    [Fact]
    public void Parse_inline_bold_and_link()
    {
        var runs = HelpDocs.Parse("Use **Play** and [the wikidot](https://dnd5e.wikidot.com).").Single().Runs;

        Assert.Equal(
            [new HelpRun("Use "), new HelpRun("Play", Bold: true), new HelpRun(" and "),
             new HelpRun("the wikidot", Link: "https://dnd5e.wikidot.com"), new HelpRun(".")],
            runs);
    }

    [Fact]
    public void Parse_image_line()
    {
        var block = HelpDocs.Parse("![The sheet](images/sheet.png)").Single();

        Assert.Equal(HelpBlockKind.Image, block.Kind);
        Assert.Equal("images/sheet.png", block.Image);
        Assert.Equal("The sheet", block.Runs[0].Text);
    }

    [Fact]
    public void Parse_unsupported_markdown_is_plain_text()
    {
        var blocks = HelpDocs.Parse("* star\n1. one\n`code`\n| a | b |\n**open");

        Assert.All(blocks, b => Assert.Equal(HelpBlockKind.Paragraph, b.Kind));
        var all = string.Concat(blocks.Select(Text));
        Assert.Contains("* star", all);
        Assert.Contains("1. one", all);
        Assert.Contains("`code`", all);
        Assert.Contains("| a | b |", all);
        Assert.Contains("**open", all);
    }

    [Theory]
    [InlineData("Creating a character", "creating-a-character")]
    [InlineData("Saves & skills!", "saves--skills")]
    [InlineData("Level 2: Spells", "level-2-spells")]
    public void Slug_matches_github(string heading, string slug) => Assert.Equal(slug, HelpDocs.Slug(heading));

    [Fact]
    public void FindAnchor_found_and_missing_returns_minus_one()
    {
        var blocks = HelpDocs.Parse("# Top\n\nText\n\n## Rests");

        Assert.Equal(2, HelpDocs.FindAnchor(blocks, "rests"));
        Assert.Equal(-1, HelpDocs.FindAnchor(blocks, "nope"));
        Assert.Equal(-1, HelpDocs.FindAnchor(blocks, null));
    }

    [Fact]
    public void ResolveLink_cases()
    {
        Assert.Equal(new HelpLink("https://x.y", null, null), HelpDocs.ResolveLink("https://x.y"));
        Assert.Equal(new HelpLink(null, "beginners-guide.md", null), HelpDocs.ResolveLink("beginners-guide.md"));
        Assert.Equal(new HelpLink(null, "tutorial.md", "creating-a-character"), HelpDocs.ResolveLink("tutorial.md#creating-a-character"));
        Assert.Equal(new HelpLink(null, null, "rests"), HelpDocs.ResolveLink("#rests"));
    }

    [Fact]
    public void Topics_are_all_bundled()
    {
        Assert.Equal(3, HelpDocs.Topics.Count);
        foreach (var topic in HelpDocs.Topics)
        {
            var blocks = HelpDocs.Load(topic.File);
            Assert.NotEmpty(blocks);
            Assert.Equal(HelpBlockKind.Heading, blocks[0].Kind);
            Assert.Equal(1, blocks[0].Level);
        }
    }

    [Fact]
    public void Bundled_docs_links_and_images_resolve()
    {
        var files = HelpDocs.Topics.Select(t => t.File).ToHashSet();
        foreach (var file in files)
        {
            foreach (var block in HelpDocs.Load(file))
            {
                if (block.Image is { } image) Assert.True(HelpDocs.LoadImage(image) is not null, $"{file}: missing {image}");
                foreach (var run in block.Runs.Where(r => r.Link is not null))
                {
                    var link = HelpDocs.ResolveLink(run.Link!);
                    Assert.True(link is not null, $"{file}: unresolvable link {run.Link}");
                    if (link!.Url is not null) continue;
                    var target = link.Doc ?? file;
                    Assert.Contains(target, files);
                    if (link.Anchor is not null)
                        Assert.True(HelpDocs.FindAnchor(HelpDocs.Load(target), link.Anchor) >= 0, $"{file}: no heading for {run.Link}");
                }
            }
        }
    }

    [Fact]
    public void Tutorial_has_creating_a_character_section() =>
        Assert.True(HelpDocs.FindAnchor(HelpDocs.Load(HelpDocs.Tutorial), HelpDocs.CreatingACharacter) >= 0);

    [Theory]
    [InlineData("file:///c:/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b")]
    public void ResolveLink_rejects_other_schemes(string target) => Assert.Null(HelpDocs.ResolveLink(target));
}
