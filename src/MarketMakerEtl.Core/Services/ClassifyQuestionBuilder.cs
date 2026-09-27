using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

internal static class ClassifyQuestionBuilder
{
    private const string ChoiceType = "choice";
    private const string NotApplicableOption = "not_applicable";
    private const string NotApplicableDescription =
        "This question does not apply to this listing given its other answers.";

    public static ClassifyQuestion Build(TaxonomyQuestion question) =>
        new(ChoiceType, question.Instructions, BuildCriteria(question));

    private static IReadOnlyDictionary<string, string> BuildCriteria(TaxonomyQuestion question)
    {
        if (question.AskWhen.Count == 0)
        {
            return question.Criteria;
        }

        return new Dictionary<string, string>(question.Criteria, StringComparer.Ordinal)
        {
            [NotApplicableOption] = NotApplicableDescription
        };
    }
}
