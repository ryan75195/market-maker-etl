namespace MarketMakerEtl.Api;

internal static class ReviewDescriptionTruncator
{
    public const int MaxLength = 1200;

    public static string? Truncate(string? description) =>
        description is null || description.Length <= MaxLength
            ? description
            : description[..MaxLength];
}
