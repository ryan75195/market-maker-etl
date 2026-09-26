using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class MercariConditionNormalizer
{
    public const string QuestionKey = "mercari_condition";

    private static readonly IReadOnlyDictionary<string, string> RawLabelsByOption =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["New"] = "new",
            ["Like new"] = "like_new",
            ["Good"] = "good",
            ["Fair"] = "fair",
            ["Poor"] = "poor",
        };

    public static IReadOnlyDictionary<string, string> Criteria { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["new"] = "Mercari's seller-stated condition is New.",
            ["like_new"] = "Mercari's seller-stated condition is Like new.",
            ["good"] = "Mercari's seller-stated condition is Good.",
            ["fair"] = "Mercari's seller-stated condition is Fair.",
            ["poor"] = "Mercari's seller-stated condition is Poor.",
        };

    public static TaxonomyQuestion ToTaxonomyQuestion() =>
        new(QuestionKey, "Mercari's seller-stated condition on the listing.", Criteria, [], null);

    public static string? Normalize(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return null;
        }

        return RawLabelsByOption.TryGetValue(condition.Trim(), out var option) ? option : null;
    }
}
