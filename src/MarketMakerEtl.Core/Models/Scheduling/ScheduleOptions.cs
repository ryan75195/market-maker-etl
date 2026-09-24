namespace MarketMakerEtl.Core.Models.Scheduling;

public sealed record ScheduleOptions(int TickMinutes, int RefreshIntervalHours);
