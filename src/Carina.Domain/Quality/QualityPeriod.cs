using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed record QualityPeriod
{
    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(366);

    public static readonly TimeSpan ShippedSpan = TimeSpan.FromHours(24);

    private QualityPeriod(DateTime from, DateTime until)
    {
        From = from;
        Until = until;
    }

    public DateTime From { get; }

    public DateTime Until { get; }

    public TimeSpan Span => Until - From;

    public static QualityPeriod? Of(DateTime? from, DateTime? until, DateTime now)
    {
        UtcTimes.Required(now, nameof(now));

        if (from is { Kind: not DateTimeKind.Utc } || until is { Kind: not DateTimeKind.Utc })
        {
            return null;
        }

        DateTime ends = until ?? now;
        DateTime begins = from ?? ends - ShippedSpan;

        return ends <= begins || ends - begins > LongestSpan ? null : new QualityPeriod(begins, ends);
    }

    public bool Holds(DateTime at) => at >= From && at < Until;
}
