using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class TaxonomyScopeAnalyzer
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetGateOpeningChoices(TaxonomyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var openingChoicesByQuestion = CollectReferencedOpeningChoices(document);
        var gatedQuestionKeys = document.Questions
            .Where(question => question.AskWhen.Count > 0)
            .Select(question => question.Key)
            .ToHashSet(StringComparer.Ordinal);

        return openingChoicesByQuestion
            .Where(pair => !gatedQuestionKeys.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value,
                StringComparer.Ordinal);
    }

    private static Dictionary<string, HashSet<string>> CollectReferencedOpeningChoices(TaxonomyDocument document)
    {
        var openingChoicesByQuestion = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var question in document.Questions)
        {
            foreach (var clause in question.AskWhen)
            {
                AddOpeningChoices(openingChoicesByQuestion, clause);
            }
        }

        return openingChoicesByQuestion;
    }

    private static void AddOpeningChoices(
        Dictionary<string, HashSet<string>> openingChoicesByQuestion, TaxonomyAskWhenClause clause)
    {
        if (!openingChoicesByQuestion.TryGetValue(clause.Question, out var choices))
        {
            choices = new HashSet<string>(StringComparer.Ordinal);
            openingChoicesByQuestion[clause.Question] = choices;
        }

        foreach (var choice in clause.AnyOf)
        {
            choices.Add(choice);
        }
    }
}
