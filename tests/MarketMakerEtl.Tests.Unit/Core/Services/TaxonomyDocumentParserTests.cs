using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TaxonomyDocumentParserTests
{
    private const string SampleJson = """
        {
         "family": "ps5-controller",
         "version": 1,
         "questions": {
          "item_type": {
           "instructions": "What is this?",
           "criteria": {
            "console": "A console.",
            "controller": "A controller."
           }
          },
          "edition": {
           "instructions": "Which edition?",
           "criteria": {
            "standard_colour": "Standard colour.",
            "not_stated": "Not stated."
           },
           "askWhen": [
            {
             "question": "item_type",
             "anyOf": ["controller"]
            }
           ],
           "notStatedMeans": "standard_colour"
          }
         }
        }
        """;

    [Test]
    public void Should_parse_family_and_version()
    {
        var document = TaxonomyDocumentParser.Parse(SampleJson);

        Assert.Multiple(() =>
        {
            Assert.That(document.Family, Is.EqualTo("ps5-controller"));
            Assert.That(document.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public void Should_parse_questions_preserving_declaration_order()
    {
        var document = TaxonomyDocumentParser.Parse(SampleJson);

        Assert.That(document.Questions.Select(q => q.Key), Is.EqualTo(new[] { "item_type", "edition" }));
    }

    [Test]
    public void Should_parse_instructions_and_criteria_for_each_question()
    {
        var document = TaxonomyDocumentParser.Parse(SampleJson);

        var itemType = document.Questions[0];
        Assert.Multiple(() =>
        {
            Assert.That(itemType.Instructions, Is.EqualTo("What is this?"));
            Assert.That(itemType.Criteria["console"], Is.EqualTo("A console."));
            Assert.That(itemType.Criteria["controller"], Is.EqualTo("A controller."));
        });
    }

    [Test]
    public void Should_parse_ask_when_and_not_stated_means()
    {
        var document = TaxonomyDocumentParser.Parse(SampleJson);

        var edition = document.Questions[1];
        Assert.Multiple(() =>
        {
            Assert.That(edition.AskWhen, Has.Count.EqualTo(1));
            Assert.That(edition.AskWhen[0].Question, Is.EqualTo("item_type"));
            Assert.That(edition.AskWhen[0].AnyOf, Is.EqualTo(new[] { "controller" }));
            Assert.That(edition.NotStatedMeans, Is.EqualTo("standard_colour"));
        });
    }

    [Test]
    public void Should_default_missing_ask_when_and_not_stated_means()
    {
        var document = TaxonomyDocumentParser.Parse(SampleJson);

        var itemType = document.Questions[0];
        Assert.Multiple(() =>
        {
            Assert.That(itemType.AskWhen, Is.Empty);
            Assert.That(itemType.NotStatedMeans, Is.Null);
        });
    }

    [Test]
    public void Should_throw_for_syntactically_invalid_json()
    {
        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse("{ not json"));

        Assert.That(exception!.Message, Does.Contain("JSON"));
    }

    [Test]
    public void Should_throw_when_the_document_root_is_not_an_object()
    {
        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse("[1, 2, 3]"));

        Assert.That(exception!.Message, Does.Contain("JSON object"));
    }

    [Test]
    public void Should_throw_when_questions_is_missing()
    {
        const string json = """{ "family": "x", "version": 1 }""";

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("questions"));
    }

    [Test]
    public void Should_throw_when_questions_is_not_an_object()
    {
        const string json = """{ "family": "x", "version": 1, "questions": "not-an-object" }""";

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("questions"));
    }

    [Test]
    public void Should_throw_when_a_question_is_not_an_object()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": { "item_type": "not-an-object" }
            }
            """;

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("item_type"));
    }

    [Test]
    public void Should_throw_when_criteria_is_not_an_object()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "item_type": {
               "instructions": "What is this?",
               "criteria": ["a", "b"]
              }
             }
            }
            """;

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("criteria"));
    }

    [Test]
    public void Should_throw_when_a_criteria_value_is_not_a_string()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "item_type": {
               "instructions": "What is this?",
               "criteria": { "console": 1, "controller": "A controller." }
              }
             }
            }
            """;

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("console"));
    }

    [Test]
    public void Should_throw_when_ask_when_is_not_an_array()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "item_type": {
               "instructions": "What is this?",
               "criteria": { "a": "A.", "b": "B." },
               "askWhen": "not-an-array"
              }
             }
            }
            """;

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("askWhen"));
    }

    [Test]
    public void Should_throw_when_an_ask_when_entry_is_missing_any_of()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "item_type": {
               "instructions": "What is this?",
               "criteria": { "a": "A.", "b": "B." },
               "askWhen": [ { "question": "item_type" } ]
              }
             }
            }
            """;

        var exception = Assert.Throws<TaxonomyParseException>(() => TaxonomyDocumentParser.Parse(json));

        Assert.That(exception!.Message, Does.Contain("anyOf"));
    }
}
