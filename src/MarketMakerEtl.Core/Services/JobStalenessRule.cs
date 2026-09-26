namespace MarketMakerEtl.Core.Services;

public static class JobStalenessRule
{
    public static bool IsStale(bool isEnabled, DateTime? lastCompletedRunUtc, int intervalHours, DateTime nowUtc)
    {
        if (!isEnabled)
        {
            return false;
        }

        if (lastCompletedRunUtc is null)
        {
            return true;
        }

        var staleThreshold = nowUtc.AddHours(-2 * intervalHours);
        return lastCompletedRunUtc < staleThreshold;
    }
}
