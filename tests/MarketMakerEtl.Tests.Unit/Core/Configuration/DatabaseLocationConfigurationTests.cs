using MarketMakerEtl.Core;
using MarketMakerEtl.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketMakerEtl.Tests.Unit.Core.Configuration;

[TestFixture]
public class DatabaseLocationConfigurationTests
{
    [Test]
    public void Should_use_the_database_location_from_configuration()
    {
        var configuredPath = Path.Combine(Path.GetTempPath(), "marketmakeretl-configured.db");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = $"Data Source={configuredPath}"
        });

        var dataSource = ResolveDataSource(configuration);

        Assert.That(dataSource, Is.EqualTo(configuredPath));
    }

    [Test]
    [NonParallelizable]
    public void Should_resolve_the_same_default_database_location_regardless_of_the_process_directory()
    {
        var originalDirectory = Environment.CurrentDirectory;
        string fromTemporaryDirectory;
        string fromRepositoryDirectory;
        try
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            fromTemporaryDirectory = ResolveDefaultDataSource();
            Environment.CurrentDirectory = originalDirectory;
            fromRepositoryDirectory = ResolveDefaultDataSource();
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(fromTemporaryDirectory), Is.True,
                "the default database location must be absolute so both hosts agree");
            Assert.That(Path.GetFileName(fromTemporaryDirectory), Is.EqualTo("marketmakeretl.db"));
            Assert.That(fromTemporaryDirectory, Is.EqualTo(fromRepositoryDirectory));
        });
    }

    private static string ResolveDataSource(IConfiguration configuration)
    {
        using var db = CreateDbContext(configuration);
        return db.Database.GetDbConnection().DataSource;
    }

    private static string ResolveDefaultDataSource()
    {
        using var db = CreateDbContext(BuildConfiguration(new Dictionary<string, string?>()));
        return db.Database.GetDbConnection().DataSource;
    }

    private static EtlDbContext CreateDbContext(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddCoreServices(configuration);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<EtlDbContext>>();
        return factory.CreateDbContext();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
