using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Tests.Integration.Jobs;

public abstract class JobsApiTestBase
{
    private string _databasePath = null!;

    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public void BaseSetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-jobs-api-{Guid.NewGuid():N}.db");
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                });
            }));
        Client = Factory.CreateClient();
    }

    [TearDown]
    public void BaseTearDown()
    {
        Client.Dispose();
        Factory.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
