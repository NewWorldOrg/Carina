using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// How much learning data is kept: the recordings it was taken from, including those thrown away since, the
/// whole seconds of them read, the bytes the segment tables take in the database, and the ended recordings
/// still waiting to be read.
/// </summary>
public sealed record LearningDataResponder(int Recordings, long Seconds, long Bytes, int Waiting)
{
    public static LearningDataResponder Of(LearningDataAmount amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        return new LearningDataResponder(
            amount.Recordings,
            (long)Math.Floor(amount.Duration.TotalSeconds),
            amount.Bytes,
            amount.Waiting);
    }
}

/// <summary>
/// Where CM, OP and ED detection stands.
/// </summary>
public sealed record SegmentStatusResponder(LearningDataResponder LearningData)
{
    public static SegmentStatusResponder Of(SegmentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SegmentStatusResponder(LearningDataResponder.Of(status.LearningData));
    }
}
