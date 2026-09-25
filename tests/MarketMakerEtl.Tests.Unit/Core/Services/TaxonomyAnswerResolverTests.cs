using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TaxonomyAnswerResolverTests
{
    private static TaxonomyDocument Ps5ControllerTaxonomy => TaxonomyDocumentParser.Parse(
        File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "taxonomies", "ps5-controller.json")));

    [Test]
    public void Should_mark_edition_and_colour_not_applicable_when_item_type_is_console()
    {
        var choices = new Dictionary<string, string>
        {
            ["item_type"] = "console",
            ["edition"] = "not_stated",
            ["colour"] = "not_stated",
            ["quantity"] = "zero",
            ["functional"] = "working"
        };

        var answers = TaxonomyAnswerResolver.Resolve(Ps5ControllerTaxonomy, choices);

        Assert.Multiple(() =>
        {
            Assert.That(AnswerFor(answers, "edition").IsApplicable, Is.False);
            Assert.That(AnswerFor(answers, "colour").IsApplicable, Is.False);
        });
    }

    [Test]
    public void Should_keep_colour_applicable_for_a_standard_dualsense_in_a_standard_colour()
    {
        var choices = new Dictionary<string, string>
        {
            ["item_type"] = "dualsense_standard",
            ["edition"] = "standard_colour",
            ["colour"] = "white",
            ["quantity"] = "one",
            ["functional"] = "working"
        };

        var answers = TaxonomyAnswerResolver.Resolve(Ps5ControllerTaxonomy, choices);

        Assert.That(AnswerFor(answers, "colour").IsApplicable, Is.True);
    }

    [Test]
    public void Should_drop_colour_for_a_dualsense_edge()
    {
        var choices = new Dictionary<string, string>
        {
            ["item_type"] = "dualsense_edge",
            ["edition"] = "standard_colour",
            ["colour"] = "white",
            ["quantity"] = "one",
            ["functional"] = "working"
        };

        var answers = TaxonomyAnswerResolver.Resolve(Ps5ControllerTaxonomy, choices);

        Assert.That(AnswerFor(answers, "colour").IsApplicable, Is.False);
    }

    [Test]
    public void Should_resolve_not_stated_via_the_not_stated_means_mapping_when_present()
    {
        var choices = new Dictionary<string, string>
        {
            ["item_type"] = "dualsense_standard",
            ["edition"] = "not_stated",
            ["colour"] = "not_stated",
            ["quantity"] = "one",
            ["functional"] = "not_stated"
        };

        var answers = TaxonomyAnswerResolver.Resolve(Ps5ControllerTaxonomy, choices);

        Assert.Multiple(() =>
        {
            Assert.That(AnswerFor(answers, "edition").ResolvedChoice, Is.EqualTo("standard_colour"));
            Assert.That(AnswerFor(answers, "functional").ResolvedChoice, Is.EqualTo("working"));
        });
    }

    [Test]
    public void Should_keep_not_stated_when_there_is_no_not_stated_means_mapping()
    {
        var choices = new Dictionary<string, string>
        {
            ["item_type"] = "dualsense_standard",
            ["edition"] = "standard_colour",
            ["colour"] = "not_stated",
            ["quantity"] = "one",
            ["functional"] = "working"
        };

        var answers = TaxonomyAnswerResolver.Resolve(Ps5ControllerTaxonomy, choices);

        Assert.That(AnswerFor(answers, "colour").ResolvedChoice, Is.EqualTo("not_stated"));
    }

    private static ResolvedTaxonomyAnswer AnswerFor(IReadOnlyList<ResolvedTaxonomyAnswer> answers, string question) =>
        answers.Single(a => a.Question == question);

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0)
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find solution root (no .slnx file found)");
    }
}
