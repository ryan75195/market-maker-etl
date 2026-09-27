namespace MarketMakerEtl.Core.Services;

public sealed class FetchInfrastructureUnavailableException : Exception
{
    public FetchInfrastructureUnavailableException()
    {
    }

    public FetchInfrastructureUnavailableException(string message)
        : base(message)
    {
    }

    public FetchInfrastructureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
