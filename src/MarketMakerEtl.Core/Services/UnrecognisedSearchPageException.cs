namespace MarketMakerEtl.Core.Services;

public sealed class UnrecognisedSearchPageException : Exception
{
    public UnrecognisedSearchPageException()
    {
    }

    public UnrecognisedSearchPageException(string message)
        : base(message)
    {
    }

    public UnrecognisedSearchPageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
