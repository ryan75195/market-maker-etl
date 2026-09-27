using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TaxonomyScopeAnalyzerTests
{
    private static TaxonomyDocument Ps5ControllerTaxonomy => TaxonomyDocumentParser.Parse(
        File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "taxonomies", "ps5-controller.json")));

    [Test]
    public void Should_derive_item_type_as_the_only_root_gate_with_both_dualsense_choices_for_the_ps5_controller_taxonomy()
    {
        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(Ps5ControllerTaxonomy);

        Assert.Multiple(() =>
        {
            Assert.That(gates.Keys, Is.EqualTo(new[] { "item_type" }));
            Assert.That(gates["item_type"], Is.EquivalentTo(new[] { "dualsense_standard", "dualsense_edge" }));
        });
    }

    [Test]
    public void Should_exclude_a_gated_question_from_the_result_even_when_it_also_gates_further_questions()
    {
        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(Ps5ControllerTaxonomy);

        Assert.Multiple(() =>
        {
            Assert.That(gates.ContainsKey("edition"), Is.False);
            Assert.That(gates.ContainsKey("colour"), Is.False);
        });
    }

    [Test]
    public void Should_return_the_single_opening_choice_for_a_question_gated_by_exactly_one_choice()
    {
        var document = new TaxonomyDocument("single-choice-test", 1,
        [
            new TaxonomyQuestion(
                "item_type", "What kind of item?", new Dictionary<string, string> { ["relevant"] = "Relevant.", ["irrelevant"] = "Irrelevant." }, [], null),
            new TaxonomyQuestion(
                "attribute",
                "Which attribute?",
                new Dictionary<string, string> { ["a"] = "A.", ["b"] = "B." },
                [new TaxonomyAskWhenClause("item_type", ["relevant"])],
                null)
        ]);

        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(document);

        Assert.Multiple(() =>
        {
            Assert.That(gates.Keys, Is.EqualTo(new[] { "item_type" }));
            Assert.That(gates["item_type"], Is.EqualTo(new[] { "relevant" }));
        });
    }

    [Test]
    public void Should_union_opening_choices_across_multiple_questions_gated_by_the_same_root_question()
    {
        var document = new TaxonomyDocument("union-test", 1,
        [
            new TaxonomyQuestion(
                "item_type", "What kind of item?", new Dictionary<string, string> { ["x"] = "X.", ["y"] = "Y.", ["z"] = "Z." }, [], null),
            new TaxonomyQuestion(
                "attribute_one",
                "Attribute one?",
                new Dictionary<string, string> { ["a"] = "A." },
                [new TaxonomyAskWhenClause("item_type", ["x"])],
                null),
            new TaxonomyQuestion(
                "attribute_two",
                "Attribute two?",
                new Dictionary<string, string> { ["b"] = "B." },
                [new TaxonomyAskWhenClause("item_type", ["y"])],
                null)
        ]);

        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(document);

        Assert.That(gates["item_type"], Is.EquivalentTo(new[] { "x", "y" }));
    }

    [Test]
    public void Should_stop_at_the_first_non_root_gate_in_a_multi_level_dependency_chain()
    {
        var document = new TaxonomyDocument("chain-test", 1,
        [
            new TaxonomyQuestion(
                "a", "Is A?", new Dictionary<string, string> { ["yes"] = "Yes.", ["no"] = "No." }, [], null),
            new TaxonomyQuestion(
                "b",
                "Is B?",
                new Dictionary<string, string> { ["b1"] = "B1.", ["b2"] = "B2." },
                [new TaxonomyAskWhenClause("a", ["yes"])],
                null),
            new TaxonomyQuestion(
                "c",
                "Is C?",
                new Dictionary<string, string> { ["c1"] = "C1.", ["c2"] = "C2." },
                [new TaxonomyAskWhenClause("b", ["b1"])],
                null)
        ]);

        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(document);

        Assert.Multiple(() =>
        {
            Assert.That(gates.Keys, Is.EqualTo(new[] { "a" }));
            Assert.That(gates["a"], Is.EqualTo(new[] { "yes" }));
        });
    }

    [Test]
    public void Should_return_an_empty_map_for_a_taxonomy_with_no_gated_questions()
    {
        var document = new TaxonomyDocument("no-gates-test", 1,
        [
            new TaxonomyQuestion(
                "item_type", "What kind of item?", new Dictionary<string, string> { ["relevant"] = "Relevant." }, [], null)
        ]);

        var gates = TaxonomyScopeAnalyzer.GetGateOpeningChoices(document);

        Assert.That(gates, Is.Empty);
    }

    [Test]
    public void Should_throw_when_the_document_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => TaxonomyScopeAnalyzer.GetGateOpeningChoices(null!));
    }

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
