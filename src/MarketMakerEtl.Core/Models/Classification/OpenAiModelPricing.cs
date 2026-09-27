namespace MarketMakerEtl.Core.Models.Classification;

public sealed record OpenAiModelPricing(decimal InputPerMillionUsd, decimal OutputPerMillionUsd);
