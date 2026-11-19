namespace Carina.Domain.Quality;

public sealed record QualityGapVerdict
{
    private QualityGapVerdict(QualityStanding standing, int count, long missedMs)
    {
        Standing = standing;
        Count = count;
        MissedMs = missedMs;
    }

    public QualityStanding Standing { get; }

    public int Count { get; }

    public long MissedMs { get; }

    public static QualityGapVerdict Of(QualityLedgerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new QualityGapVerdict(Judged(row), row.Gaps, row.MissedMs);
    }

    private static QualityStanding Judged(QualityLedgerRow row)
    {
        if (!row.Counters.Measured)
        {
            return QualityStanding.Unmeasured;
        }

        return row.Gaps > 0 ? QualityStanding.Warning : QualityStanding.Good;
    }
}
