using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed record QualityTrendBucket(DateTime From, DateTime Until);

public sealed class QualityTrendFrame
{
    public const int MostPoints = QualityGroupQuery.MostPerPage;

    public const int ShippedDays = 1;

    private QualityTrendFrame(QualityPeriod period, QualityTrendStep step, IReadOnlyList<QualityTrendBucket> buckets)
    {
        Period = period;
        Step = step;
        Buckets = buckets;
    }

    public static int MostDays => (int)QualityPeriod.LongestSpan.TotalDays - 1;

    public static DateTime Grid => DateTime.UnixEpoch + BroadcastDay.StartsAt - JapanTimeZone.Instance.BaseUtcOffset;

    public QualityPeriod Period { get; }

    public QualityTrendStep Step { get; }

    public TimeSpan Length => QualityTrendSteps.Length(Step);

    public IReadOnlyList<QualityTrendBucket> Buckets { get; }

    public static QualityTrendFrame? Over(int? days, DateTime now, QualityTrendStep finest)
    {
        UtcTimes.Required(now, nameof(now));

        IReadOnlyList<QualityTrendStep> steps = QualityTrendSteps.NoFinerThan(finest);
        int asked = days ?? ShippedDays;

        if (asked < 1 || asked > MostDays)
        {
            return null;
        }

        DateTime from = QualityWindows.StartOf(now - TimeSpan.FromDays(asked), QualityWindow.Hour);

        if (QualityPeriod.Of(from, now, now) is not { } period)
        {
            return null;
        }

        foreach (QualityTrendStep step in steps)
        {
            if (Count(period, step) <= MostPoints)
            {
                return new QualityTrendFrame(period, step, Cut(period, step));
            }
        }

        throw new InvalidOperationException(
            $"Every period of at most {MostDays} days fits in {MostPoints} points at the widest step, and this one did not.");
    }

    public static DateTime StartOf(DateTime at, QualityTrendStep step)
    {
        long length = QualityTrendSteps.Length(step).Ticks;
        long offset = (((at.Ticks - Grid.Ticks) % length) + length) % length;

        return new DateTime(at.Ticks - offset, DateTimeKind.Utc);
    }

    public int IndexOf(DateTime at)
    {
        DateTime first = StartOf(Period.From, Step);

        return at < first || at >= Period.Until
            ? -1
            : (int)((StartOf(at, Step) - first).Ticks / Length.Ticks);
    }

    private static int Count(QualityPeriod period, QualityTrendStep step)
    {
        long length = QualityTrendSteps.Length(step).Ticks;
        long covered = (period.Until - StartOf(period.From, step)).Ticks;

        return (int)((covered + length - 1) / length);
    }

    private static IReadOnlyList<QualityTrendBucket> Cut(QualityPeriod period, QualityTrendStep step)
    {
        TimeSpan length = QualityTrendSteps.Length(step);
        List<QualityTrendBucket> cut = [];

        for (DateTime start = StartOf(period.From, step); start < period.Until; start += length)
        {
            DateTime end = start + length;

            cut.Add(new QualityTrendBucket(
                start < period.From ? period.From : start,
                end > period.Until ? period.Until : end));
        }

        return cut;
    }
}
