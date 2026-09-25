using System.Text.Json;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Taxonomies;

namespace MarketMakerEtl.Core.Services;

public sealed class ListingClassificationService : IListingClassificationService
{
    private const string ChoiceType = "choice";

    private readonly IJobStore _jobs;
    private readonly IProductFamilyStore _families;
    private readonly IListingClassificationStore _classifications;
    private readonly IListingClassifierClient _client;
    private readonly ClassifierOptions _options;

    public ListingClassificationService(
        IJobStore jobs,
        IProductFamilyStore families,
        IListingClassificationStore classifications,
        IListingClassifierClient client,
        ClassifierOptions options)
    {
        _jobs = jobs;
        _families = families;
        _classifications = classifications;
        _client = client;
        _options = options;
    }

    public async Task<ClassificationTickResult> ClassifyPending(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.BaseUrl))
        {
            return new ClassificationTickResult(0, 0, 0, []);
        }

        var jobs = (await _jobs.GetEffectivelyEnabledJobs(ct))
            .Where(job => job.ProductFamilyId.HasValue)
            .ToList();

        return await ClassifyJobs(jobs, ct);
    }

    private async Task<ClassificationTickResult> ClassifyJobs(IReadOnlyList<JobView> jobs, CancellationToken ct)
    {
        var jobsProcessed = 0;
        var listingsSelected = 0;
        var listingsClassified = 0;
        var failures = new List<ClassificationBatchFailure>();
        var remainingBudget = _options.MaxListingsPerTick;

        foreach (var job in jobs)
        {
            if (remainingBudget <= 0)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            var outcome = await ClassifyJob(job, remainingBudget, ct);
            if (outcome is null)
            {
                continue;
            }

            jobsProcessed++;
            listingsSelected += outcome.Selected;
            listingsClassified += outcome.Classified;
            remainingBudget -= outcome.Selected;
            failures.AddRange(outcome.Failures);
        }

        return new ClassificationTickResult(jobsProcessed, listingsSelected, listingsClassified, failures);
    }

    private async Task<JobClassificationOutcome?> ClassifyJob(JobView job, int budget, CancellationToken ct)
    {
        var family = await _families.GetFamily(job.ProductFamilyId!.Value, ct);
        if (family?.LatestTaxonomyVersion is null)
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

        var classified = 0;
        var failures = new List<ClassificationBatchFailure>();

        foreach (var batch in Chunk(targets, _options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            classified += await ClassifyBatch(
                job.Id, family.ModelName, family.LatestTaxonomyVersion.Id, taxonomy, batch, failures, ct);
        }

        return new JobClassificationOutcome(targets.Count, classified, failures);
    }

    private async Task<int> ClassifyBatch(
        int jobId,
        string modelName,
        int taxonomyVersionId,
        TaxonomyDocument taxonomy,
        IReadOnlyList<ListingClassificationTarget> batch,
        List<ClassificationBatchFailure> failures,
        CancellationToken ct)
    {
        try
        {
            var request = BuildRequest(modelName, taxonomy, batch);
            var response = await _client.Classify(request, ct);
            var humanChoicesByListing = await _classifications.GetHumanChoices(
                batch.Select(target => target.ListingEntityId).ToList(), ct)
                ?? new Dictionary<int, IReadOnlyDictionary<string, string>>();
            var batchItems = BuildBatchItems(taxonomy, taxonomyVersionId, batch, response, humanChoicesByListing);
            await _classifications.UpsertBatch(batchItems, ct);
            return batch.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(new ClassificationBatchFailure(jobId, batch.Count, ex.Message));
            return 0;
        }
    }

    private static ClassifyRequest BuildRequest(
        string modelName, TaxonomyDocument taxonomy, IReadOnlyList<ListingClassificationTarget> batch)
    {
        var questions = taxonomy.Questions.ToDictionary(
            question => question.Key,
            question => new ClassifyQuestion(ChoiceType, question.Instructions, question.Criteria));
        var states = batch.Select(ClassificationStateBuilder.BuildState).ToList();

        return new ClassifyRequest(modelName, questions, states);
    }

    private static IReadOnlyList<ListingClassificationBatchItem> BuildBatchItems(
        TaxonomyDocument taxonomy,
        int taxonomyVersionId,
        IReadOnlyList<ListingClassificationTarget> batch,
        ClassifyResponse response,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> humanChoicesByListing)
    {
        var items = new List<ListingClassificationBatchItem>(batch.Count);

        for (var index = 0; index < batch.Count; index++)
        {
            var target = batch[index];
            humanChoicesByListing.TryGetValue(target.ListingEntityId, out var humanChoices);
            items.Add(BuildBatchItem(taxonomy, taxonomyVersionId, target, response.Results[index], humanChoices));
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

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var offset = 0; offset < source.Count; offset += size)
        {
            yield return source.Skip(offset).Take(size).ToList();
        }
    }

    private sealed record JobClassificationOutcome(
        int Selected, int Classified, IReadOnlyList<ClassificationBatchFailure> Failures);
}
