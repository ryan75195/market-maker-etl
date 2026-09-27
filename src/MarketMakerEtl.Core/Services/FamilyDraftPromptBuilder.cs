using System.Text;
using System.Text.Json;
using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Services;

internal static class FamilyDraftPromptBuilder
{
    public static FamilyDraftMessages Build(FamilyDraftPrompt prompt) =>
        new(BuildSystemPrompt(), BuildUserPrompt(prompt));

    private static string BuildSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "You design listing-classification taxonomies for a Mercari reselling tool. " +
            "Given a product family name, a search term, and a sample of real listings, " +
            "propose a taxonomy document in the exact JSON shape used by this system.");
        builder.AppendLine();
        AppendShapeRules(builder);
        AppendWorkedExample(builder);
        AppendOutputContract(builder);
        return builder.ToString();
    }

    private static void AppendShapeRules(StringBuilder builder)
    {
        builder.AppendLine("## Shape rules");
        builder.AppendLine(
            "- Top-level object has \"family\" (kebab-case), \"version\" (1), \"questions\" (ordered object), " +
            "and an optional \"guidance\" string.");
        builder.AppendLine(
            "- The first question must be named \"item_type\" and must classify what the listing actually is. " +
            "Exactly one of its option keys may be \"in scope\" for this family; every other option key must be " +
            "an out-of-scope catch-all (other item types, accessories, parts, unrelated goods).");
        builder.AppendLine(
            "- Every other question drives resale price for the in-scope item type and must include \"askWhen\": " +
            "[{ \"question\": \"item_type\", \"anyOf\": [\"<the one in-scope option key>\"] }] so it is only asked " +
            "when the item is in scope.");
        builder.AppendLine(
            "- Every question needs at least 2 \"criteria\" entries with snake_case keys and a plain-English " +
            "description of when each applies. Gated questions must include a \"not_stated\" option for when the " +
            "text does not say.");
        builder.AppendLine(
            "- Also include a top-level \"dealGroupBy\" string: a comma-separated list of the gated question keys " +
            "(not item_type) that meaningfully change resale price, used to group comparable listings together.");
        builder.AppendLine();
    }

    private static void AppendWorkedExample(StringBuilder builder)
    {
        builder.AppendLine($"## Worked example: search term \"{FamilyDraftExampleTaxonomy.SearchTerm}\", family \"{FamilyDraftExampleTaxonomy.FamilyName}\"");
        builder.AppendLine(FamilyDraftExampleTaxonomy.TaxonomyJson);
        builder.AppendLine($"With \"dealGroupBy\": \"{FamilyDraftExampleTaxonomy.DealGroupBy}\"");
        builder.AppendLine();
    }

    private static void AppendOutputContract(StringBuilder builder)
    {
        builder.AppendLine(
            "Return only the JSON object for the new family (questions object plus the top-level " +
            "\"dealGroupBy\" field), with no surrounding prose or markdown fences.");
    }

    private static string BuildUserPrompt(FamilyDraftPrompt prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Family name: {prompt.FamilyName}");
        builder.AppendLine($"Search term: {prompt.SearchTerm}");
        AppendFeedback(builder, prompt);
        builder.AppendLine();
        builder.AppendLine("Sample listings from this search:");
        builder.AppendLine(JsonSerializer.Serialize(prompt.Sample));
        return builder.ToString();
    }

    private static void AppendFeedback(StringBuilder builder, FamilyDraftPrompt prompt)
    {
        if (prompt.PreviousTaxonomyJson is null || prompt.Feedback is null)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Previous draft taxonomy:");
        builder.AppendLine(prompt.PreviousTaxonomyJson);
        builder.AppendLine();
        builder.AppendLine($"Reviewer feedback to address: {prompt.Feedback}");
    }
}
