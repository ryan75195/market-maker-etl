namespace MarketMakerEtl.Core.Services;

public sealed class ListingNotFoundException : Exception
{
    public ListingNotFoundException()
    {
    }

    public ListingNotFoundException(string message)
        : base(message)
    {
    }

    public ListingNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
