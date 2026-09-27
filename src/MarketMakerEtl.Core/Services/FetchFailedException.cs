namespace MarketMakerEtl.Core.Services;

public sealed class FetchFailedException : Exception
{
    public FetchFailedException()
    {
    }

    public FetchFailedException(string message)
        : base(message)
    {
    }

    public FetchFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
