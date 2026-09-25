namespace MarketMakerEtl.Core.Services;

public sealed class ListingClassifierException : Exception
{
    public bool IsTimeout { get; private init; }

    public ListingClassifierException()
    {
    }

    public ListingClassifierException(string message)
        : base(message)
    {
    }

    public ListingClassifierException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static ListingClassifierException Timeout(string message) =>
        new(message) { IsTimeout = true };
}
