using MarketMakerEtl.Core.Data;
using Microsoft.Data.Sqlite;

namespace MarketMakerEtl.Tests.Unit.Core.Data;

[TestFixture]
public class SqlitePragmaConnectionInterceptorTests
{
    [Test]
    public void Should_set_busy_timeout_before_switching_a_writable_file_connection_to_wal()
    {
        var interceptor = new SqlitePragmaConnectionInterceptor(1234);
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(Path.GetTempPath(), $"mm-etl-pragma-order-{Guid.NewGuid():N}.db")}");

        var script = interceptor.BuildPragmaScript(connection);

        var busyTimeoutIndex = script.IndexOf("busy_timeout", StringComparison.OrdinalIgnoreCase);
        var walIndex = script.IndexOf("journal_mode", StringComparison.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(busyTimeoutIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(walIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(busyTimeoutIndex, Is.LessThan(walIndex));
        });
    }

    [Test]
    public void Should_only_set_busy_timeout_for_a_readonly_connection()
    {
        var interceptor = new SqlitePragmaConnectionInterceptor(1234);
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(Path.GetTempPath(), $"mm-etl-pragma-order-{Guid.NewGuid():N}.db")};Mode=ReadOnly");

        var script = interceptor.BuildPragmaScript(connection);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("busy_timeout"));
            Assert.That(script, Does.Not.Contain("journal_mode"));
        });
    }
}
