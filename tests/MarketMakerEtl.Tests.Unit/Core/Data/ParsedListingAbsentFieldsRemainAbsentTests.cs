using MarketMakerEtl.Core.Data;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class ParsedListingAbsentFieldsRemainAbsentTests
{
    private const string CardMissingImageAndBuyingFormat = """
        <ul>
          <li class="s-card" data-viewport="1">
            <a class="s-card__link" href="https://www.ebay.co.uk/itm/222222222222">link</a>
            <span class="s-card__title">Nintendo Switch OLED</span>
            <span class="s-card__price">£210.00</span>
            <span class="s-card__condition">Used</span>
          </li>
        </ul>
        """;

    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-{Guid.NewGuid():N}.db");
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
    public async Task Should_store_absent_fields_as_absent_rather_than_guessing()
    {
        var parsed = new EbaySearchParser().Parse(CardMissingImageAndBuyingFormat).Listings.Single();

        var store = CreateStore();
        var jobId = await store.EnsureJob("switch", CancellationToken.None);
        await store.UpsertListings(jobId, [parsed], CancellationToken.None);
        var stored = (await store.GetListings(jobId, CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(parsed.PrimaryImageUrl, Is.Null);
            Assert.That(parsed.BuyingFormat, Is.Null);
            Assert.That(stored.Condition, Is.EqualTo("Used"));
            Assert.That(stored.PrimaryImageUrl, Is.Null);
            Assert.That(stored.BuyingFormat, Is.Null);
        });
    }

    private ScrapeStore CreateStore() =>
        new(
            _provider.GetRequiredService<IDbContextFactory<EtlDbContext>>(),
            new ScrapeRunStateService());
}
