using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class CategoryStoreTests
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-category-store-{Guid.NewGuid():N}.db");
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
    public async Task Should_create_a_category_with_its_name_and_enabled_flag()
    {
        var store = CreateStore();

        var category = await store.CreateCategory("Consoles", true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(category.Name, Is.EqualTo("Consoles"));
            Assert.That(category.IsEnabled, Is.True);
        });
    }

    [Test]
    public async Task Should_list_every_created_category()
    {
        var store = CreateStore();
        await store.CreateCategory("Consoles", true, CancellationToken.None);
        await store.CreateCategory("Toys", true, CancellationToken.None);

        var categories = await store.GetCategories(CancellationToken.None);

        Assert.That(categories.Select(c => c.Name), Is.EquivalentTo(new[] { "Consoles", "Toys" }));
    }

    [Test]
    public async Task Should_return_a_single_category_by_id()
    {
        var store = CreateStore();
        var created = await store.CreateCategory("Consoles", true, CancellationToken.None);

        var category = await store.GetCategory(created.Id, CancellationToken.None);

        Assert.That(category!.Name, Is.EqualTo("Consoles"));
    }

    [Test]
    public async Task Should_update_a_categorys_name_and_enabled_flag()
    {
        var store = CreateStore();
        var created = await store.CreateCategory("Consoles", true, CancellationToken.None);

        var updated = await store.UpdateCategory(created.Id, "Retro Consoles", false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(updated!.Name, Is.EqualTo("Retro Consoles"));
            Assert.That(updated.IsEnabled, Is.False);
        });
    }

    [Test]
    public async Task Should_delete_a_category_and_no_longer_return_it()
    {
        var store = CreateStore();
        var created = await store.CreateCategory("Consoles", true, CancellationToken.None);

        var deleted = await store.DeleteCategory(created.Id, CancellationToken.None);
        var afterDelete = await store.GetCategory(created.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(afterDelete, Is.Null);
        });
    }

    [Test]
    public async Task Should_toggle_a_categorys_enabled_flag()
    {
        var store = CreateStore();
        var created = await store.CreateCategory("Consoles", true, CancellationToken.None);

        var disabled = await store.SetCategoryEnabled(created.Id, false, CancellationToken.None);
        var enabled = await store.SetCategoryEnabled(created.Id, true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disabled!.IsEnabled, Is.False);
            Assert.That(enabled!.IsEnabled, Is.True);
        });
    }

    private CategoryStore CreateStore() =>
        new(_provider.GetRequiredService<IDbContextFactory<EtlDbContext>>());
}
