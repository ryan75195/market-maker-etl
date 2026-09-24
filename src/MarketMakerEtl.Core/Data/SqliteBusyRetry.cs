using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

internal static class SqliteBusyRetry
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(25);

    public static async Task ExecuteAsync(Func<Task> operation, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsTransientLock(ex))
            {
                await Task.Delay(BaseDelay * attempt, ct);
            }
        }
    }

    private static bool IsTransientLock(Exception exception)
    {
        var current = exception.InnerException;

        while (current is not null)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 })
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
