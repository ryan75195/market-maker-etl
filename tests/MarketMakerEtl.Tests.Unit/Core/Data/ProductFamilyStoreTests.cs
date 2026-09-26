using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ProductFamilyStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-family-store-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<EtlDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Test]
    public async Task Should_create_a_family_with_no_taxonomy_version_yet()
    {
        var store = CreateStore();

        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        Assert.Multiple(() =>
        {
            Assert.That(family.Key, Is.EqualTo("ps5-controller"));
            Assert.That(family.Name, Is.EqualTo("PS5 Controller"));
            Assert.That(family.ModelName, Is.EqualTo("ps5-controller"));
            Assert.That(family.LatestTaxonomyVersion, Is.Null);
        });
    }

    [Test]
    public async Task Should_return_null_when_creating_a_family_with_a_duplicate_key()
    {
        var store = CreateStore();
        await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None);

        var duplicate = await store.CreateFamily("ps5-controller", "Other Name", "other-model", CancellationToken.None);

        Assert.That(duplicate, Is.Null);
    }

    [Test]
    public async Task Should_update_a_familys_name_and_model_name()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        var updated = await store.UpdateFamily(family.Id, "PS5 Controller v2", "ps5-controller-v2", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated!.Name, Is.EqualTo("PS5 Controller v2"));
            Assert.That(updated.ModelName, Is.EqualTo("ps5-controller-v2"));
        });
    }

    [Test]
    public async Task Should_leave_fields_unchanged_when_updating_with_nulls()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        var updated = await store.UpdateFamily(family.Id, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated!.Name, Is.EqualTo("PS5 Controller"));
            Assert.That(updated.ModelName, Is.EqualTo("ps5-controller"));
        });
    }

    [Test]
    public async Task Should_return_null_when_updating_an_unknown_family()
    {
        var store = CreateStore();

        var updated = await store.UpdateFamily(999999, "New Name", null, CancellationToken.None);

        Assert.That(updated, Is.Null);
    }

    [Test]
    public async Task Should_list_every_family_with_its_latest_taxonomy_version()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;
        await store.AddTaxonomyVersion(family.Id, "{}", CancellationToken.None);
        await store.AddTaxonomyVersion(family.Id, "{\"v\":2}", CancellationToken.None);

        var families = await store.GetFamilies(CancellationToken.None);

        var listed = families.Single(f => f.Id == family.Id);
        Assert.That(listed.LatestTaxonomyVersion!.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_return_a_single_family_by_id()
    {
        var store = CreateStore();
        var created = (await store.CreateFamily("iphone-15", "iPhone 15", "iphone-15", CancellationToken.None))!;

        var family = await store.GetFamily(created.Id, CancellationToken.None);

        Assert.That(family!.Key, Is.EqualTo("iphone-15"));
    }

    [Test]
    public async Task Should_return_null_for_an_unknown_family_id()
    {
        var store = CreateStore();

        var family = await store.GetFamily(999999, CancellationToken.None);

        Assert.That(family, Is.Null);
    }

    [Test]
    public async Task Should_increment_taxonomy_versions_per_family_starting_at_one()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        var first = await store.AddTaxonomyVersion(family.Id, "{\"a\":1}", CancellationToken.None);
        var second = await store.AddTaxonomyVersion(family.Id, "{\"a\":2}", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first!.Version, Is.EqualTo(1));
            Assert.That(second!.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Should_return_null_when_adding_a_taxonomy_version_to_an_unknown_family()
    {
        var store = CreateStore();

        var version = await store.AddTaxonomyVersion(999999, "{}", CancellationToken.None);

        Assert.That(version, Is.Null);
    }

    [Test]
    public async Task Should_round_trip_the_stored_questions_json_unchanged()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;
        const string questionsJson = "{\"family\":\"ps5-controller\",\"questions\":{\"a\":1}}";

        var added = await store.AddTaxonomyVersion(family.Id, questionsJson, CancellationToken.None);
        var fetched = await store.GetTaxonomyVersion(family.Id, added!.Version, CancellationToken.None);

        Assert.That(fetched!.QuestionsJson, Is.EqualTo(questionsJson));
    }

    [Test]
    public async Task Should_return_null_for_an_unknown_taxonomy_version()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        var version = await store.GetTaxonomyVersion(family.Id, 5, CancellationToken.None);

        Assert.That(version, Is.Null);
    }

    [Test]
    public async Task Should_return_the_highest_version_as_the_latest_taxonomy_version()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;
        await store.AddTaxonomyVersion(family.Id, "{\"v\":1}", CancellationToken.None);
        await store.AddTaxonomyVersion(family.Id, "{\"v\":2}", CancellationToken.None);

        var latest = await store.GetLatestTaxonomyVersion(family.Id, CancellationToken.None);

        Assert.That(latest!.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_return_null_latest_taxonomy_version_for_a_family_with_none()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;

        var latest = await store.GetLatestTaxonomyVersion(family.Id, CancellationToken.None);

        Assert.That(latest, Is.Null);
    }

    [Test]
    public async Task Should_return_a_taxonomy_version_by_its_id()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;
        var added = (await store.AddTaxonomyVersion(family.Id, "{\"v\":1}", CancellationToken.None))!;

        var fetched = await store.GetTaxonomyVersionById(added.Id, CancellationToken.None);

        Assert.That(fetched!.QuestionsJson, Is.EqualTo("{\"v\":1}"));
    }

    [Test]
    public async Task Should_return_null_for_an_unknown_taxonomy_version_id()
    {
        var store = CreateStore();

        var fetched = await store.GetTaxonomyVersionById(999999, CancellationToken.None);

        Assert.That(fetched, Is.Null);
    }

    [Test]
    public async Task Should_set_and_clear_a_jobs_family()
    {
        var store = CreateStore();
        var family = (await store.CreateFamily("ps5-controller", "PS5 Controller", "ps5-controller", CancellationToken.None))!;
        var jobId = await CreateJob();

        var setResult = await store.SetJobFamily(jobId, family.Id, CancellationToken.None);
        var clearResult = await store.SetJobFamily(jobId, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(setResult, Is.True);
            Assert.That(clearResult, Is.True);
        });
    }

    [Test]
    public async Task Should_return_false_when_setting_the_family_of_an_unknown_job()
    {
        var store = CreateStore();

        var result = await store.SetJobFamily(999999, null, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    private async Task<int> CreateJob()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>().CreateDbContextAsync();
        var job = new ScrapeJobEntity { SearchTerm = "ps5 controller", CreatedUtc = DateTime.UtcNow };
        db.ScrapeJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private ProductFamilyStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
}
