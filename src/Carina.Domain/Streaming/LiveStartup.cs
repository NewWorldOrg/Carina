using System.Buffers.Binary;

namespace Carina.Domain.Streaming;

public sealed record LiveStartupMark(
    LiveStartupSegment Segment,
    TimeSpan? ReachedAt,
    TimeSpan? Took,
    LiveStartupSegment? TookFrom)
{
    public bool Reached => ReachedAt is not null;
}

public enum LiveStartupFault
{
    NotAsLongAsAProgressReport = 1,

    AStateNoSegmentCanBeIn = 2,

    ASegmentThatIsNotReachedButCarriesATime = 3,
}

public sealed record LiveStartupReading(LiveStartup? Startup, LiveStartupFault? Fault)
{
    public static LiveStartupReading Read(LiveStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        return new LiveStartupReading(startup, null);
    }

    public static LiveStartupReading Broken(LiveStartupFault fault)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A progress report is refused for one of the reasons named here.");
        }

        return new LiveStartupReading(null, fault);
    }
}

public sealed class LiveStartup
{
    public const int MarkLength = 5;

    public const int PayloadLength = MarkLength * 5;

    private const byte NotReached = 0;

    private const byte WasReached = 1;

    private readonly IReadOnlyDictionary<LiveStartupSegment, TimeSpan> reached;

    private LiveStartup(IReadOnlyDictionary<LiveStartupSegment, TimeSpan> reached)
    {
        this.reached = reached;
    }

    public static LiveStartup NotStarted { get; } =
        new(new Dictionary<LiveStartupSegment, TimeSpan>());

    public bool InProgress => !Reached(LiveStartupSegments.Last);

    public IReadOnlyList<LiveStartupMark> Timeline => [.. LiveStartupSegments.InOrder.Select(Mark)];

    public bool Reached(LiveStartupSegment segment) => reached.ContainsKey(segment);

    public TimeSpan? At(LiveStartupSegment segment)
        => reached.TryGetValue(segment, out TimeSpan elapsed) ? elapsed : null;

    public TimeSpan? Took(LiveStartupSegment segment)
        => At(segment) is { } arrived ? arrived - LatestBehind(segment).At : null;

    public LiveStartupMark Mark(LiveStartupSegment segment)
    {
        if (!Enum.IsDefined(segment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(segment),
                segment,
                "The startup runs through one of the segments named here.");
        }

        return new LiveStartupMark(
            segment,
            At(segment),
            Took(segment),
            Reached(segment) ? LatestBehind(segment).Segment : null);
    }

    public LiveStartup Reaching(LiveStartupSegment segment, TimeSpan elapsed)
    {
        if (!Enum.IsDefined(segment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(segment),
                segment,
                "The startup runs through one of the segments named here.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        Dictionary<LiveStartupSegment, TimeSpan> next = new(reached) { [segment] = elapsed };

        return new LiveStartup(next);
    }

    public byte[] ToProgressPayload()
    {
        byte[] payload = new byte[PayloadLength];
        int at = 0;

        foreach (LiveStartupSegment segment in LiveStartupSegments.InOrder)
        {
            if (At(segment) is { } elapsed)
            {
                payload[at] = WasReached;
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(at + 1), Milliseconds(elapsed));
            }

            at += MarkLength;
        }

        return payload;
    }

    public static LiveStartupReading ReadProgress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not PayloadLength)
        {
            return LiveStartupReading.Broken(LiveStartupFault.NotAsLongAsAProgressReport);
        }

        Dictionary<LiveStartupSegment, TimeSpan> reached = [];
        int at = 0;

        foreach (LiveStartupSegment segment in LiveStartupSegments.InOrder)
        {
            byte state = payload[at];
            uint milliseconds = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(at + 1, 4));

            switch (state)
            {
                case WasReached:
                    reached[segment] = TimeSpan.FromMilliseconds(milliseconds);
                    break;
                case NotReached when milliseconds is not 0:
                    return LiveStartupReading.Broken(LiveStartupFault.ASegmentThatIsNotReachedButCarriesATime);
                case NotReached:
                    break;
                default:
                    return LiveStartupReading.Broken(LiveStartupFault.AStateNoSegmentCanBeIn);
            }

            at += MarkLength;
        }

        return LiveStartupReading.Read(new LiveStartup(reached));
    }

    private (LiveStartupSegment? Segment, TimeSpan At) LatestBehind(LiveStartupSegment segment)
    {
        (LiveStartupSegment? Segment, TimeSpan At) latest = (null, TimeSpan.Zero);

        foreach (LiveStartupSegment behind in LiveStartupSegments.Behind(segment))
        {
            (LiveStartupSegment? Segment, TimeSpan At) waited = At(behind) is { } was ? (behind, was) : LatestBehind(behind);

            if (waited.Segment is not null && (latest.Segment is null || waited.At > latest.At))
            {
                latest = waited;
            }
        }

        return latest;
    }

    private static uint Milliseconds(TimeSpan elapsed)
    {
        double milliseconds = elapsed.TotalMilliseconds;

        return milliseconds >= uint.MaxValue ? uint.MaxValue : (uint)milliseconds;
    }
}
