namespace MarketMakerEtl.Core.Models.Classification;

public sealed record OpenAiPricingOptions(
    IReadOnlyDictionary<string, OpenAiModelPricing> ModelPrices,
    OpenAiModelPricing DefaultPrice)
{
    public OpenAiModelPricing Resolve(string model) =>
        ModelPrices.TryGetValue(model, out var pricing) ? pricing : DefaultPrice;
}
