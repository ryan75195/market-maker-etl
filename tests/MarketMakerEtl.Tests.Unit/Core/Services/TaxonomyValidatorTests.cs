using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class TaxonomyValidatorTests
{
    [Test]
    public void Should_accept_a_valid_document()
    {
        const string json = """
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
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Errors, Is.Empty);
        });
    }

    [Test]
    public void Should_reject_empty_questions()
    {
        const string json = """{ "family": "x", "version": 1, "questions": {} }""";

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("questions"));
        });
    }

    [Test]
    public void Should_reject_fewer_than_two_criteria_entries()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "item_type": {
               "instructions": "What is this?",
               "criteria": { "only_one": "Only one option." }
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("item_type"));
        });
    }

    [Test]
    public void Should_reject_ask_when_referencing_a_later_question()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "a": "A.", "b": "B." },
               "askWhen": [ { "question": "second", "anyOf": ["c"] } ]
              },
              "second": {
               "instructions": "Second?",
               "criteria": { "c": "C.", "d": "D." }
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("earlier"));
        });
    }

    [Test]
    public void Should_reject_ask_when_referencing_an_unknown_question()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "a": "A.", "b": "B." },
               "askWhen": [ { "question": "does_not_exist", "anyOf": ["a"] } ]
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Should_reject_ask_when_with_an_unknown_option()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "a": "A.", "b": "B." }
              },
              "second": {
               "instructions": "Second?",
               "criteria": { "c": "C.", "d": "D." },
               "askWhen": [ { "question": "first", "anyOf": ["not_a_real_option"] } ]
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("not_a_real_option"));
        });
    }

    [Test]
    public void Should_reject_not_stated_means_without_a_not_stated_option()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "a": "A.", "b": "B." },
               "notStatedMeans": "a"
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("not_stated"));
        });
    }

    [Test]
    public void Should_reject_not_stated_means_pointing_to_an_unknown_option()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "a": "A.", "not_stated": "Not stated." },
               "notStatedMeans": "does_not_exist"
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("does_not_exist"));
        });
    }

    [Test]
    public void Should_reject_option_keys_that_are_not_snake_case()
    {
        const string json = """
            {
             "family": "x",
             "version": 1,
             "questions": {
              "first": {
               "instructions": "First?",
               "criteria": { "CamelCase": "Bad.", "another": "Ok." }
              }
             }
            }
            """;

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("snake_case"));
        });
    }

    [TestCase("ps5-controller.json")]
    [TestCase("iphone-15.json")]
    public void Should_accept_the_committed_taxonomy_files(string fileName)
    {
        var path = Path.Combine(FindSolutionRoot(), "docs", "taxonomies", fileName);
        var json = File.ReadAllText(path);

        var result = TaxonomyValidator.Validate(TaxonomyDocumentParser.Parse(json));

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
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
