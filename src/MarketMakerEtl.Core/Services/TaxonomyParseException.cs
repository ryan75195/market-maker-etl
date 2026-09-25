namespace MarketMakerEtl.Core.Services;

public sealed class TaxonomyParseException : Exception
{
    public TaxonomyParseException()
    {
    }

    public TaxonomyParseException(string message)
        : base(message)
    {
    }

    public TaxonomyParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
