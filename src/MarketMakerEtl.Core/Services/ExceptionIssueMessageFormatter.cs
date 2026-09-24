namespace MarketMakerEtl.Core.Services;

internal static class ExceptionIssueMessageFormatter
{
    public static string Describe(Exception exception)
    {
        var innermost = FindInnermost(exception);

        return ReferenceEquals(innermost, exception)
            ? exception.Message
            : $"{exception.Message} (root cause: {innermost.GetType().Name}: {innermost.Message})";
    }

    private static Exception FindInnermost(Exception exception)
    {
        var current = exception;

        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }
}
