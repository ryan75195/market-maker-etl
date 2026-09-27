using System.Text.Json;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingClassificationService : IListingClassificationService
{
    private readonly IProductFamilyStore _families;
    private readonly IListingClassificationStore _classifications;
    private readonly IListingClassifierClient _client;
    private readonly IClassificationThrottleService _throttle;
    private readonly OpenAiOptions _openAiOptions;

    public ListingClassificationService(
        IProductFamilyStore families,
        IListingClassificationStore classifications,
        IListingClassifierClient client,
        IClassificationThrottleService throttle,
        OpenAiOptions openAiOptions)
    {
        _families = families;
        _classifications = classifications;
        _client = client;
        _throttle = throttle;
        _openAiOptions = openAiOptions;
    }

    public async Task<ClassificationTickResult> ClassifyPending(
        Action<ClassificationBatchFailure> onBatchFailure, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_openAiOptions.ApiKey) || _throttle.IsBackingOffFailures())
        {
            return new ClassificationTickResult(0, 0, 0, []);
        }

        var jobs = (await _families.GetJobsWithFamily(ct))
            .Where(job => job.ProductFamilyId.HasValue)
            .ToList();

        var result = await ClassifyJobs(jobs, onBatchFailure, ct);
        _throttle.ObserveTickResult(result);
        return result;
    }

    private async Task<ClassificationTickResult> ClassifyJobs(
        IReadOnlyList<JobView> jobs, Action<ClassificationBatchFailure> onBatchFailure, CancellationToken ct)
    {
        var jobsProcessed = 0;
        var listingsSelected = 0;
        var listingsClassified = 0;
        var failures = new List<ClassificationBatchFailure>();
        var probing = _throttle.IsProbingAfterFailures();
        var remainingBudget = _throttle.ResolveTickBudget();

        foreach (var job in jobs)
        {
            if (remainingBudget <= 0)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            var outcome = await ClassifyJob(job, remainingBudget, onBatchFailure, ct);
            if (outcome is null)
            {
                continue;
            }

            jobsProcessed++;
            listingsSelected += outcome.Selected;
            listingsClassified += outcome.Classified;
            remainingBudget -= outcome.Selected;
            failures.AddRange(outcome.Failures);

            if (probing && outcome.Selected > 0 && outcome.Classified == 0)
            {
                break;
            }
        }

        return new ClassificationTickResult(jobsProcessed, listingsSelected, listingsClassified, failures);
    }

    private async Task<JobClassificationOutcome?> ClassifyJob(
        JobView job, int budget, Action<ClassificationBatchFailure> onBatchFailure, CancellationToken ct)
    {
        var family = await _families.GetFamily(job.ProductFamilyId!.Value, ct);
        if (family?.LatestTaxonomyVersion is null || family.State != FamilyState.Active)
        {
            return null;
        }

        var taxonomy = TaxonomyDocumentParser.Parse(family.LatestTaxonomyVersion.QuestionsJson);
        var targets = await _classifications.GetListingsNeedingClassification(
            job.Id, family.LatestTaxonomyVersion.Id, budget, ct);

        if (targets.Count == 0)
        {
            return new JobClassificationOutcome(0, 0, []);
        }

        return await ClassifyBatch(job.Id, family.ModelName, family.LatestTaxonomyVersion.Id, taxonomy, targets, onBatchFailure, ct);
    }

    private async Task<JobClassificationOutcome> ClassifyBatch(
        int jobId,
        string modelName,
        int taxonomyVersionId,
        TaxonomyDocument taxonomy,
        IReadOnlyList<ListingClassificationTarget> targets,
        Action<ClassificationBatchFailure> onBatchFailure,
        CancellationToken ct)
    {
        try
        {
            var request = BuildRequest(modelName, taxonomy, targets);
            var response = await _client.Classify(request, ct);
            var humanChoicesByListing = await _classifications.GetHumanChoices(
                targets.Select(target => target.ListingEntityId).ToList(), ct)
                ?? new Dictionary<int, IReadOnlyDictionary<string, string>>();

            var succeeded = BuildBatchItems(taxonomy, taxonomyVersionId, targets, response, humanChoicesByListing, out var failedCount);
            await _classifications.UpsertBatch(succeeded, ct);

            var batchSucceeded = failedCount == 0;
            await _classifications.RecordBatchOutcome(batchSucceeded, ct);

            if (!batchSucceeded)
            {
                var failure = new ClassificationBatchFailure(jobId, failedCount, "OpenAI failed to classify some listings.");
                onBatchFailure(failure);
                return new JobClassificationOutcome(targets.Count, succeeded.Count, [failure]);
            }

            return new JobClassificationOutcome(targets.Count, succeeded.Count, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _classifications.RecordBatchOutcome(false, ct);
            var failure = new ClassificationBatchFailure(jobId, targets.Count, ex.Message);
            onBatchFailure(failure);
            return new JobClassificationOutcome(targets.Count, 0, [failure]);
        }
    }

    private static ClassifyRequest BuildRequest(
        string modelName, TaxonomyDocument taxonomy, IReadOnlyList<ListingClassificationTarget> targets)
    {
        var questions = taxonomy.Questions.ToDictionary(
            question => question.Key,
            ClassifyQuestionBuilder.Build);
        var states = targets.Select(ClassificationStateBuilder.BuildState).ToList();

        return new ClassifyRequest(modelName, questions, states, taxonomy.Guidance);
    }

    private static IReadOnlyList<ListingClassificationBatchItem> BuildBatchItems(
        TaxonomyDocument taxonomy,
        int taxonomyVersionId,
        IReadOnlyList<ListingClassificationTarget> targets,
        ClassifyResponse response,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> humanChoicesByListing,
        out int failedCount)
    {
        var items = new List<ListingClassificationBatchItem>(targets.Count);
        failedCount = 0;

        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var result = response.Results[index];
            if (result.Error is not null)
            {
                failedCount++;
                continue;
            }

            humanChoicesByListing.TryGetValue(target.ListingEntityId, out var humanChoices);
            items.Add(BuildBatchItem(taxonomy, taxonomyVersionId, target, result, humanChoices));
        }

        return items;
    }

    private static ListingClassificationBatchItem BuildBatchItem(
        TaxonomyDocument taxonomy,
        int taxonomyVersionId,
        ListingClassificationTarget target,
        ClassifyResult result,
        IReadOnlyDictionary<string, string>? humanChoices)
    {
        var choices = result.Answers.ToDictionary(answer => answer.Key, answer => answer.Value.Choice);
        var resolved = TaxonomyAnswerResolver.Resolve(taxonomy, choices, humanChoices);
        var rows = resolved.Select(answer => BuildRow(answer, result.Answers[answer.Question])).ToList();

        return new ListingClassificationBatchItem(target.ListingEntityId, taxonomyVersionId, rows);
    }

    private static ListingClassificationRow BuildRow(ResolvedTaxonomyAnswer answer, ClassifyAnswer modelAnswer) =>
        new(
            answer.Question,
            answer.Choice,
            answer.ResolvedChoice,
            answer.IsApplicable,
            modelAnswer.Confidence,
            modelAnswer.Agreement,
            JsonSerializer.Serialize(modelAnswer.Probabilities));

    private sealed record JobClassificationOutcome(
        int Selected, int Classified, IReadOnlyList<ClassificationBatchFailure> Failures);
}
