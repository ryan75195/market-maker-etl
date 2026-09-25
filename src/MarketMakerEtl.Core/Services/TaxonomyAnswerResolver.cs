using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class TaxonomyAnswerResolver
{
    private const string NotStatedOption = "not_stated";

    public static IReadOnlyList<ResolvedTaxonomyAnswer> Resolve(
        TaxonomyDocument document, IReadOnlyDictionary<string, string> choices)
    {
        var answers = new List<ResolvedTaxonomyAnswer>(document.Questions.Count);
        var rawByQuestion = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var question in document.Questions)
        {
            answers.Add(ResolveQuestion(question, choices, rawByQuestion));
        }

        return answers;
    }

    private static ResolvedTaxonomyAnswer ResolveQuestion(
        TaxonomyQuestion question,
        IReadOnlyDictionary<string, string> choices,
        Dictionary<string, string> rawByQuestion)
    {
        if (!choices.TryGetValue(question.Key, out var rawChoice))
        {
            return new ResolvedTaxonomyAnswer(question.Key, string.Empty, null, false);
        }

        rawByQuestion[question.Key] = rawChoice;
        var isApplicable = IsApplicable(question, rawByQuestion);
        var resolvedChoice = isApplicable ? ResolveChoice(question, rawChoice) : null;

        return new ResolvedTaxonomyAnswer(question.Key, rawChoice, resolvedChoice, isApplicable);
    }

    private static bool IsApplicable(TaxonomyQuestion question, IReadOnlyDictionary<string, string> rawByQuestion) =>
        question.AskWhen.All(clause =>
            rawByQuestion.TryGetValue(clause.Question, out var referencedChoice) &&
            clause.AnyOf.Contains(referencedChoice, StringComparer.Ordinal));

    private static string ResolveChoice(TaxonomyQuestion question, string rawChoice) =>
        rawChoice == NotStatedOption && question.NotStatedMeans is not null
            ? question.NotStatedMeans
            : rawChoice;
}
