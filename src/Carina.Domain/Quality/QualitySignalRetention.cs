namespace Carina.Domain.Quality;

public static class QualitySignalRetention
{
    public static DateTime? SamplesTakenBefore(
        DateTime now,
        TimeSpan keepFor,
        IReadOnlyDictionary<QualityWindow, DateTime?> rolledUpThrough)
    {
        ArgumentNullException.ThrowIfNull(rolledUpThrough);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(keepFor, TimeSpan.Zero);

        DateTime cutoff = now - keepFor;

        foreach (QualityWindow window in QualityWindows.All)
        {
            if (!rolledUpThrough.TryGetValue(window, out DateTime? through) || through is not { } reached)
            {
                return null;
            }

            if (reached < cutoff)
            {
                cutoff = reached;
            }
        }

        return cutoff;
    }

    public static DateTime? WindowsStartedBefore(DateTime now, TimeSpan? keepFor)
    {
        if (keepFor is not { } kept)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(kept, TimeSpan.Zero, nameof(keepFor));

        return now - kept;
    }
}
