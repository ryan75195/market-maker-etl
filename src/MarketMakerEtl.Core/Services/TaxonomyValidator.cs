using System.Text.RegularExpressions;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public static class TaxonomyValidator
{
    private static readonly Regex SnakeCasePattern = new(@"^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);

    public static TaxonomyValidationResult Validate(TaxonomyDocument document)
    {
        var errors = new List<string>();
        if (document.Questions.Count == 0)
        {
            errors.Add("questions must be a non-empty ordered object.");
            return new TaxonomyValidationResult(false, errors);
        }

        var positions = BuildPositions(document.Questions);
        foreach (var question in document.Questions)
        {
            ValidateQuestion(question, positions, errors);
        }

        return new TaxonomyValidationResult(errors.Count == 0, errors);
    }

    private static IReadOnlyDictionary<string, QuestionPosition> BuildPositions(
        IReadOnlyList<TaxonomyQuestion> questions)
    {
        var positions = new Dictionary<string, QuestionPosition>();
        for (var index = 0; index < questions.Count; index++)
        {
            positions[questions[index].Key] = new QuestionPosition(index, questions[index]);
        }

        return positions;
    }

    private static void ValidateQuestion(
        TaxonomyQuestion question,
        IReadOnlyDictionary<string, QuestionPosition> positions,
        List<string> errors)
    {
        ValidateInstructions(question, errors);
        ValidateCriteria(question, errors);
        ValidateAskWhen(question, positions, errors);
        ValidateNotStatedMeans(question, errors);
    }

    private static void ValidateInstructions(TaxonomyQuestion question, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(question.Instructions))
        {
            errors.Add($"Question '{question.Key}' must have non-empty instructions.");
        }
    }

    private static void ValidateCriteria(TaxonomyQuestion question, List<string> errors)
    {
        if (question.Criteria.Count < 2)
        {
            errors.Add($"Question '{question.Key}' must have at least 2 criteria entries.");
        }

        foreach (var optionKey in question.Criteria.Keys)
        {
            if (!SnakeCasePattern.IsMatch(optionKey))
            {
                errors.Add($"Question '{question.Key}' has option key '{optionKey}' that is not snake_case.");
            }
        }
    }

    private static void ValidateAskWhen(
        TaxonomyQuestion question,
        IReadOnlyDictionary<string, QuestionPosition> positions,
        List<string> errors)
    {
        var currentIndex = positions[question.Key].Index;
        foreach (var clause in question.AskWhen)
        {
            ValidateAskWhenClause(question, clause, currentIndex, positions, errors);
        }
    }

    private static void ValidateAskWhenClause(
        TaxonomyQuestion question,
        TaxonomyAskWhenClause clause,
        int currentIndex,
        IReadOnlyDictionary<string, QuestionPosition> positions,
        List<string> errors)
    {
        if (!positions.TryGetValue(clause.Question, out var referenced) || referenced.Index >= currentIndex)
        {
            errors.Add(
                $"Question '{question.Key}' askWhen references '{clause.Question}', which must appear earlier in the order.");
            return;
        }

        if (clause.AnyOf.Count == 0)
        {
            errors.Add($"Question '{question.Key}' askWhen for '{clause.Question}' must have a non-empty anyOf list.");
            return;
        }

        foreach (var optionKey in clause.AnyOf)
        {
            if (!referenced.Question.Criteria.ContainsKey(optionKey))
            {
                errors.Add(
                    $"Question '{question.Key}' askWhen references unknown option '{optionKey}' of '{clause.Question}'.");
            }
        }
    }

    private static void ValidateNotStatedMeans(TaxonomyQuestion question, List<string> errors)
    {
        if (question.NotStatedMeans is null)
        {
            return;
        }

        if (!question.Criteria.ContainsKey("not_stated"))
        {
            errors.Add($"Question '{question.Key}' has notStatedMeans but no 'not_stated' option.");
            return;
        }

        if (question.NotStatedMeans == "not_stated" || !question.Criteria.ContainsKey(question.NotStatedMeans))
        {
            errors.Add($"Question '{question.Key}' notStatedMeans points to unknown option '{question.NotStatedMeans}'.");
        }
    }
}

internal sealed record QuestionPosition(int Index, TaxonomyQuestion Question);
