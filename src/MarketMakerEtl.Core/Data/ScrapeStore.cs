using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class ScrapeStore : IScrapeStore
{
    private const string ActiveStatus = "Active";

    private readonly IDbContextFactory<EtlDbContext> _factory;
    private readonly IScrapeRunStateService _states;

    public ScrapeStore(IDbContextFactory<EtlDbContext> factory, IScrapeRunStateService states)
    {
        _factory = factory;
        _states = states;
    }

    public async Task<int> EnsureJob(string searchTerm, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var job = await db.ScrapeJobs.FirstOrDefaultAsync(j => j.SearchTerm == searchTerm, ct);

        if (job is null)
        {
            job = new ScrapeJobEntity { SearchTerm = searchTerm, CreatedUtc = DateTime.UtcNow };
            db.ScrapeJobs.Add(job);
            await db.SaveChangesAsync(ct);
        }

        return job.Id;
    }

    public async Task<int> EnqueueRun(int jobId, string searchTerm, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var run = new ScrapeRunEntity
        {
            JobId = jobId,
            SearchTerm = searchTerm,
            Status = nameof(ScrapeRunStatus.Queued),
            StartedUtc = DateTime.UtcNow
        };
        db.ScrapeRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task<ScrapeRunWork?> ClaimNextQueuedRun(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var run = await db.ScrapeRuns
            .Where(r => r.Status == nameof(ScrapeRunStatus.Queued))
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (run is null)
        {
            return null;
        }

        _states.EnsureCanTransition(ScrapeRunStatus.Queued, ScrapeRunStatus.Running);
        run.Status = nameof(ScrapeRunStatus.Running);
        await db.SaveChangesAsync(ct);
        return new ScrapeRunWork(run.Id, run.JobId, run.SearchTerm);
    }

    public Task CompleteRun(int runId, CancellationToken ct) =>
        UpdateStatus(runId, ScrapeRunStatus.Completed, null, ct);

    public Task FailRun(int runId, string error, CancellationToken ct) =>
        UpdateStatus(runId, ScrapeRunStatus.Failed, error, ct);

    public async Task UpsertListings(int jobId, IReadOnlyList<ListingSummary> listings, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        foreach (var listing in listings.GroupBy(l => l.ListingId).Select(g => g.Last()))
        {
            var existing = await db.Listings
                .FirstOrDefaultAsync(l => l.ListingId == listing.ListingId, ct);
            Apply(db, jobId, listing, existing);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<ScrapeRunView?> GetRun(int runId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var run = await db.ScrapeRuns.FindAsync([runId], ct);

        return run is null
            ? null
            : new ScrapeRunView(
                run.Id,
                run.JobId,
                run.SearchTerm,
                Enum.Parse<ScrapeRunStatus>(run.Status),
                run.ErrorMessage);
    }

    public async Task<IReadOnlyList<ListingSummary>> GetListings(int jobId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.Listings
            .Where(l => l.ScrapeJobId == jobId)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

        return listings
            .Select(l => new ListingSummary(
                l.ListingId,
                l.Title,
                l.Price,
                l.Currency,
                l.Url,
                l.IsSold,
                l.Condition,
                l.PrimaryImageUrl,
                l.BuyingFormat))
            .ToList();
    }

    public async Task<IReadOnlyList<ListingRefreshTarget>> GetActiveListings(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.Listings
            .Where(l => l.ItemStatus == null || l.ItemStatus == ActiveStatus)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

        return listings
            .Select(l => new ListingRefreshTarget(l.Id, l.ListingId, l.Url, l.ItemStatus))
            .ToList();
    }

    public async Task RecordStatusChange(int listingEntityId, string status, decimal? price, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FindAsync([listingEntityId], ct);

        if (listing is null)
        {
            return;
        }

        var current = string.IsNullOrWhiteSpace(listing.ItemStatus) ? ActiveStatus : listing.ItemStatus;

        if (string.Equals(current, status, StringComparison.Ordinal))
        {
            return;
        }

        listing.ItemStatus = status;
        listing.Price = price ?? listing.Price;
        listing.UpdatedUtc = DateTime.UtcNow;
        db.ListingStatusChanges.Add(new ListingStatusChangeEntity
        {
            ListingEntityId = listingEntityId,
            Status = status,
            ChangedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(
        EtlDbContext db,
        int jobId,
        ListingSummary listing,
        ListingEntity? existing)
    {
        if (existing is null)
        {
            db.Listings.Add(new ListingEntity
            {
                ListingId = listing.ListingId,
                ScrapeJobId = jobId,
                Title = listing.Title,
                Price = listing.Price,
                Currency = listing.Currency,
                Url = listing.Url,
                IsSold = listing.IsSold,
                Condition = listing.Condition,
                PrimaryImageUrl = listing.PrimaryImageUrl,
                BuyingFormat = listing.BuyingFormat,
                CreatedUtc = DateTime.UtcNow
            });
            return;
        }

        existing.Title = listing.Title;
        existing.Price = listing.Price;
        existing.Currency = listing.Currency;
        existing.Url = listing.Url;
        existing.IsSold = listing.IsSold;
        existing.Condition = listing.Condition;
        existing.PrimaryImageUrl = listing.PrimaryImageUrl;
        existing.BuyingFormat = listing.BuyingFormat;
        existing.UpdatedUtc = DateTime.UtcNow;
    }

    private async Task UpdateStatus(
        int runId,
        ScrapeRunStatus status,
        string? error,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var run = await db.ScrapeRuns.FindAsync([runId], ct);

        if (run is null)
        {
            return;
        }

        _states.EnsureCanTransition(Enum.Parse<ScrapeRunStatus>(run.Status), status);
        run.Status = status.ToString();
        run.ErrorMessage = error;
        run.CompletedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
