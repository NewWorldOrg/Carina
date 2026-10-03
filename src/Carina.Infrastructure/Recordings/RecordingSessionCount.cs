using Carina.Contracts;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// What one recording session counted, in the shape the recording keeps it.
/// </summary>
public sealed record RecordingSessionCount(
    DropCounters Counters,
    DropTimeline Positions,
    long? Scrambled,
    long Overflows,
    DateTime Opened,
    string DeviceId)
{
    public static RecordingSessionCount Of(DriverHello hello, SessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(session);

        RecordingSessionDto reading = RecordingSessionDto.Of(hello, session);

        return new RecordingSessionCount(
            reading.CcMeasured
                ? DropCounters.Counted(reading.CcDropped ?? 0, reading.CcTotal ?? 0)
                : DropCounters.Unmeasured,
            Placed(reading.Positions),
            reading.ScrambledPackets,
            reading.EovfCount,
            session.StartedAt.UtcDateTime,
            session.DeviceId);
    }

    /// <summary>
    /// The count of the session a recording ended on, or null when the driver's greeting or the
    /// session is not there to read it from.
    /// </summary>
    public static RecordingSessionCount? Ending(DriverHello? hello, SessionSnapshot? session)
        => hello is null || session is null ? null : Of(hello, session);

    public void Into(Recording recording, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(recording);

        recording.Measure(Counters, Positions, Scrambled, Overflows, at, Opened);
    }

    public bool CountedAnything => Counters.Measured || Scrambled is not null;

    /// <summary>
    /// Writes the count a session ended with onto a recording about to be given its outcome, unless
    /// the session counted nothing or the recording was already counted at <paramref name="at"/> or
    /// later.
    /// </summary>
    public void LastInto(Recording recording, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (!CountedAnything || at <= (recording.MeasuredUpdatedAt ?? recording.StartedAtActual))
        {
            return;
        }

        RecordingResumption.Adopt(recording, DeviceId);
        Into(recording, at);
    }

    private static DropTimeline Placed(DropPositionsDto? positions)
        => positions is null
            ? DropTimeline.Unlocated
            : DropTimeline.Rehydrate(
                positions.AnchorPcr,
                [.. positions.Buckets.Select(bucket =>
                    new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled))],
                [.. positions.Reanchors.Select(reanchor =>
                    new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After))]);
}
