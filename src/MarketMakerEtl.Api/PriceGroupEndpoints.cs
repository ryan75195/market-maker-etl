using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Api;

public static class PriceGroupEndpoints
{
    private const int DefaultSoldDays = 30;
    private const int DefaultMinSold = 1;
    private const int DefaultTake = 50;

    public static WebApplication MapPriceGroupEndpoints(this WebApplication app)
    {
        app.MapGet("/api/families/{familyId:int}/price-groups", GetPriceGroups);
        app.MapGet("/api/families/{familyId:int}/price-groups/listings", GetGroupListings);

        return app;
    }

    private static async Task<IResult> GetPriceGroups(
        int familyId,
        string[]? where,
        string? by,
        IProductFamilyStore families,
        IPriceGroupQueryService priceGroups,
        CancellationToken ct,
        int soldDays = DefaultSoldDays,
        bool includeUncertain = false,
        int minSold = DefaultMinSold,
        string? trim = null)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family is null)
        {
            return Results.NotFound();
        }

        var whereClauses = where ?? [];
        var byList = SplitBy(by);
        if (family.LatestTaxonomyVersion is null)
        {
            return whereClauses.Length == 0 && byList.Count == 0
                ? Results.Ok(Array.Empty<PriceGroupSummary>())
                : QuestionValidationProblem(["This family has no taxonomy version yet."]);
        }

        var questions = LoadQuestions(family.LatestTaxonomyVersion.QuestionsJson);
        var errors = new List<string>();
        var whereMap = ParseWhere(whereClauses, questions, errors);
        ValidateBy(byList, questions, errors);
        if (!TryParseTrim(trim, out var trimIqr))
        {
            errors.Add("trim must be 'iqr'.");
        }

        if (errors.Count > 0)
        {
            return QuestionValidationProblem(errors);
        }

        var query = new PriceGroupQuery(
            family.LatestTaxonomyVersion.Id, whereMap, byList, soldDays, includeUncertain, minSold, trimIqr);
        return Results.Ok(await priceGroups.GetPriceGroups(query, ct));
    }

    private static async Task<IResult> GetGroupListings(
        int familyId,
        string[]? where,
        IProductFamilyStore families,
        IPriceGroupQueryService priceGroups,
        CancellationToken ct,
        string status = "active",
        int take = DefaultTake)
    {
        var family = await families.GetFamily(familyId, ct);
        if (family?.LatestTaxonomyVersion is null)
        {
            return Results.NotFound();
        }

        if (!TryParseStatus(status, out var parsedStatus))
        {
            return QuestionValidationProblem(["status must be 'active' or 'sold'."]);
        }

        var questions = LoadQuestions(family.LatestTaxonomyVersion.QuestionsJson);
        var errors = new List<string>();
        var whereMap = ParseWhere(where ?? [], questions, errors);
        if (errors.Count > 0)
        {
            return QuestionValidationProblem(errors);
        }

        var query = new PriceGroupListingsQuery(
            family.LatestTaxonomyVersion.Id, whereMap, parsedStatus, take, DefaultSoldDays, false);
        return Results.Ok(await priceGroups.GetGroupListings(query, ct));
    }

    private static IReadOnlyDictionary<string, TaxonomyQuestion> LoadQuestions(string questionsJson)
    {
        var questions = TaxonomyDocumentParser.Parse(questionsJson).Questions
            .ToDictionary(q => q.Key, StringComparer.Ordinal);
        questions[MercariConditionNormalizer.QuestionKey] = MercariConditionNormalizer.ToTaxonomyQuestion();
        return questions;
    }

    private static bool TryParseTrim(string? trim, out bool trimIqr)
    {
        if (string.IsNullOrEmpty(trim))
        {
            trimIqr = false;
            return true;
        }

        trimIqr = string.Equals(trim, "iqr", StringComparison.OrdinalIgnoreCase);
        return trimIqr;
    }

    private static Dictionary<string, string> ParseWhere(
        string[] where, IReadOnlyDictionary<string, TaxonomyQuestion> questions, List<string> errors)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var clause in where)
        {
            var separatorIndex = clause.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == clause.Length - 1)
            {
                errors.Add($"'{clause}' must be in the form question:option.");
                continue;
            }

            var question = clause[..separatorIndex];
            var option = clause[(separatorIndex + 1)..];
            ValidateQuestionOption(questions, question, option, errors);
            parsed[question] = option;
        }

        return parsed;
    }

    private static void ValidateQuestionOption(
        IReadOnlyDictionary<string, TaxonomyQuestion> questions, string question, string option, List<string> errors)
    {
        if (!questions.TryGetValue(question, out var found))
        {
            errors.Add($"Unknown question '{question}'.");
            return;
        }

        if (!found.Criteria.ContainsKey(option))
        {
            errors.Add($"Unknown option '{option}' for question '{question}'.");
        }
    }

    private static void ValidateBy(
        IReadOnlyList<string> by, IReadOnlyDictionary<string, TaxonomyQuestion> questions, List<string> errors)
    {
        foreach (var question in by)
        {
            if (!questions.ContainsKey(question))
            {
                errors.Add($"Unknown question '{question}'.");
            }
        }
    }

    private static IReadOnlyList<string> SplitBy(string? by) =>
        string.IsNullOrWhiteSpace(by)
            ? []
            : by.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseStatus(string status, out PriceGroupListingStatus parsed)
    {
        switch (status)
        {
            case "active":
                parsed = PriceGroupListingStatus.Active;
                return true;
            case "sold":
                parsed = PriceGroupListingStatus.Sold;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static IResult QuestionValidationProblem(IReadOnlyList<string> errors) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["Questions"] = errors.ToArray() });
}
