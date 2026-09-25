using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

public static class ClassificationStateBuilder
{
    private const int MaxDescriptionLength = 1200;

    public static ClassifyListingState BuildState(ListingClassificationTarget target) =>
        new(target.Title, BuildMercariCategory(target), target.Brand, TruncateDescription(target.Description));

    private static string? BuildMercariCategory(ListingClassificationTarget target)
    {
        var parts = new[] { target.Category0Name, target.Category1Name, target.Category2Name }
            .Where(part => !string.IsNullOrEmpty(part));
        var joined = string.Join(" > ", parts);
        return joined.Length == 0 ? null : joined;
    }

    private static string? TruncateDescription(string? description) =>
        description is null || description.Length <= MaxDescriptionLength
            ? description
            : description[..MaxDescriptionLength];
}
