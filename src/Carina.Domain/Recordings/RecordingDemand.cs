using Carina.Contracts;
using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingDemand
{
    public RecordingDemand(TunerKind kind, DateTime from, DateTime until)
    {
        DateTime start = UtcTimes.Required(from, nameof(from));
        DateTime finish = UtcTimes.Required(until, nameof(until));

        if (finish <= start)
        {
            throw new ArgumentException(
                "A recording window ends after it starts, so there is no weight to put on one that does not.",
                nameof(until));
        }

        Bitrate = ExpectedBitrate.Of(kind);
        Kind = kind;
        From = start;
        Until = finish;
    }

    public TunerKind Kind { get; }

    public DateTime From { get; }

    public DateTime Until { get; }

    public ExpectedBitrate Bitrate { get; }

    public TimeSpan Remaining(DateTime asOf)
    {
        DateTime now = UtcTimes.Required(asOf, nameof(asOf));
        DateTime start = now > From ? now : From;

        return start < Until ? Until - start : TimeSpan.Zero;
    }

    public Int128 HeaviestBytes(DateTime asOf) => Bitrate.MostBytesOver(Remaining(asOf));
}
