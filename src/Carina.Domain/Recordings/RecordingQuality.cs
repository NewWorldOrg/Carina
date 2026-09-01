namespace Carina.Domain.Recordings;

public enum QualityLevel
{
    Good = 1,

    Unmeasured = 2,

    Warning = 3,

    MayNotBeWatchable = 4,
}

public sealed record QualityShare(double Warning, double Unwatchable);

public static class QualityShares
{
    public static QualityShare PacketsLost { get; } = new(0.0002, 0.01);

    public static QualityShare PacketsLeftScrambled { get; } = new(0.0005, 0.01);
}

public static class RecordingQuality
{
    public static QualityLevel Of(DropCounters counters, long? scrambledPackets)
    {
        ArgumentNullException.ThrowIfNull(counters);

        if (counters.Total is not { } total || counters.Dropped is not { } dropped)
        {
            return QualityLevel.Unmeasured;
        }

        if (total is 0)
        {
            return QualityLevel.MayNotBeWatchable;
        }

        QualityLevel lost = Read(dropped, total, QualityShares.PacketsLost);
        QualityLevel encrypted = scrambledPackets is { } left
            ? Read(left, total, QualityShares.PacketsLeftScrambled)
            : QualityLevel.Unmeasured;

        return lost > encrypted ? lost : encrypted;
    }

    private static QualityLevel Read(long counted, long total, QualityShare share)
    {
        double of = (double)counted / total;

        if (of >= share.Unwatchable)
        {
            return QualityLevel.MayNotBeWatchable;
        }

        return of >= share.Warning ? QualityLevel.Warning : QualityLevel.Good;
    }
}
