namespace MarketMakerEtl.Core.Services;

public sealed class ListingClassifierException : Exception
{
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
}
