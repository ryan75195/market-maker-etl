namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealScanTickResult(int FamiliesScanned, int GroupsEvaluated, int SignalsCreated);
