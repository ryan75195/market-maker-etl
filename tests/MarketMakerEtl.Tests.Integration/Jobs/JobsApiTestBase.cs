using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MarketMakerEtl.Tests.Integration.Jobs;

public abstract class JobsApiTestBase
{
    private string _databasePath = null!;
    private WebApplicationFactory<Program> _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public void BaseSetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"mm-etl-jobs-api-{Guid.NewGuid():N}.db");
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = $"Data Source={_databasePath}"
                });
            }));
        Client = _factory.CreateClient();
    }

    [TearDown]
    public void BaseTearDown()
    {
        Client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
