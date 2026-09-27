using System.Text;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiPromptBuilder
{
    public static string Build(string family, IReadOnlyDictionary<string, ClassifyQuestion> questions, string? guidance)
    {
        var builder = new StringBuilder();
        AppendIntroduction(builder, family);
        foreach (var (key, question) in questions)
        {
            AppendQuestion(builder, key, question);
        }

        AppendGuidance(builder, guidance);
        AppendOutputInstructions(builder);
        return builder.ToString();
    }

    private static void AppendIntroduction(StringBuilder builder, string family)
    {
        builder.AppendLine(
            $"You are labelling Mercari listings for the '{family}' product family.");
        builder.AppendLine(
            "Judge only from the text given (title, description, category, brand). " +
            "Output exactly one of the listed option keys for each question.");
        builder.AppendLine();
    }

    private static void AppendQuestion(StringBuilder builder, string key, ClassifyQuestion question)
    {
        builder.AppendLine($"### {key}");
        builder.AppendLine(question.Instructions);
        foreach (var (optionKey, description) in question.Criteria)
        {
            builder.AppendLine($"- {optionKey}: {description}");
        }

        builder.AppendLine();
    }

    private static void AppendGuidance(StringBuilder builder, string? guidance)
    {
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return;
        }

        builder.AppendLine("## Additional guidance");
        builder.AppendLine(guidance);
        builder.AppendLine();
    }

    private static void AppendOutputInstructions(StringBuilder builder)
    {
        builder.AppendLine(
            "For each listing also set \"ambiguous\" to an empty string normally, " +
            "or a short note (under 8 words) naming the question you had to guess on.");
        builder.AppendLine(
            "Return a JSON object {\"labels\": [...]}, one entry per listing in the same order as given, " +
            "each including the listing's \"id\".");
    }
}
