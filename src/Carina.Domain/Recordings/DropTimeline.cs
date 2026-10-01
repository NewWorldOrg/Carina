namespace Carina.Domain.Recordings;

public sealed record DropBucket(int Second, long Continuity, long Scrambled);

public sealed record PcrReanchor(int Second, long Before, long After);

public sealed record DropTimeline
{
    public const long PcrWrapsAt = 8_589_934_592;

    public const long TicksPerSecond = 90_000;

    public static readonly TimeSpan SeamTolerance = TimeSpan.FromSeconds(30);

    private DropTimeline(long? anchorPcr, IReadOnlyList<DropBucket> buckets, IReadOnlyList<PcrReanchor> reanchors)
    {
        AnchorPcr = anchorPcr;
        Buckets = buckets;
        Reanchors = reanchors;
    }

    public static DropTimeline Unlocated { get; } = new(null, [], []);

    public long? AnchorPcr { get; }

    public IReadOnlyList<DropBucket> Buckets { get; }

    public IReadOnlyList<PcrReanchor> Reanchors { get; }

    public bool Located => AnchorPcr is not null;

    public long Continuity => Buckets.Sum(bucket => bucket.Continuity);

    public long Scrambled => Buckets.Sum(bucket => bucket.Scrambled);

    public static DropTimeline AnchoredAt(long pcr) => Rehydrate(pcr, [], []);

    public static DropTimeline Rehydrate(
        long? anchorPcr,
        IReadOnlyList<DropBucket> buckets,
        IReadOnlyList<PcrReanchor> reanchors)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(reanchors);

        if (anchorPcr is null)
        {
            return buckets.Count is 0 && reanchors.Count is 0
                ? Unlocated
                : throw new ArgumentException(
                    "Nothing said where in the stream these were, so there is no position to carry.",
                    nameof(anchorPcr));
        }

        WithinTheClock(anchorPcr.Value, nameof(anchorPcr));

        int previous = -1;
        foreach (DropBucket bucket in buckets)
        {
            if (bucket.Second <= previous)
            {
                throw new ArgumentException(
                    "A timeline reads forwards and names each second once.",
                    nameof(buckets));
            }

            if (bucket.Continuity < 0 || bucket.Scrambled < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(buckets), bucket, "A second cannot lose a negative number of packets.");
            }

            if (bucket.Continuity is 0 && bucket.Scrambled is 0)
            {
                throw new ArgumentException(
                    "A timeline names only the seconds where something happened.",
                    nameof(buckets));
            }

            previous = bucket.Second;
        }

        previous = -1;
        foreach (PcrReanchor reanchor in reanchors)
        {
            if (reanchor.Second <= previous)
            {
                throw new ArgumentException(
                    "A timeline reads forwards and names each second once.",
                    nameof(reanchors));
            }

            WithinTheClock(reanchor.Before, nameof(reanchors));
            WithinTheClock(reanchor.After, nameof(reanchors));

            previous = reanchor.Second;
        }

        return new DropTimeline(anchorPcr, [.. buckets], [.. reanchors]);
    }

    /// <summary>
    /// This timeline carried on by a later session's. The later session's seconds are placed as far along as its
    /// clock began after this one's; when that disagrees with <paramref name="into"/>, how far into the recording
    /// the later session opened, by more than <see cref="SeamTolerance"/>, they are placed by
    /// <paramref name="into"/> and the jump is kept as a re-anchor.
    /// </summary>
    public DropTimeline Then(DropTimeline later, TimeSpan into)
    {
        ArgumentNullException.ThrowIfNull(later);

        if (later.AnchorPcr is not { } began)
        {
            return this;
        }

        if (AnchorPcr is not { } anchor)
        {
            return later;
        }

        int from = Reanchors.Count > 0 ? Reanchors[^1].Second : 0;
        long origin = Reanchors.Count > 0 ? Reanchors[^1].After : anchor;
        long byTheClock = from + (Wrapped(began - origin) / TicksPerSecond);
        long opened = Math.Max(0, (long)into.TotalSeconds);
        bool believed = Math.Abs(byTheClock - opened) <= (long)SeamTolerance.TotalSeconds;
        int seam = (int)(believed ? byTheClock : Math.Max(opened, LastNamedSecond));

        SortedDictionary<int, DropBucket> buckets = [];

        foreach (DropBucket bucket in Buckets.Concat(later.Buckets.Select(one => one with { Second = one.Second + seam })))
        {
            buckets[bucket.Second] = buckets.TryGetValue(bucket.Second, out DropBucket? held)
                ? new DropBucket(bucket.Second, held.Continuity + bucket.Continuity, held.Scrambled + bucket.Scrambled)
                : bucket;
        }

        IEnumerable<PcrReanchor> joined = later.Reanchors.Select(one => one with { Second = one.Second + seam });

        if (!believed)
        {
            joined = joined.Prepend(new PcrReanchor(seam, Wrapped(origin + ((seam - from) * TicksPerSecond)), began));
        }

        SortedDictionary<int, PcrReanchor> reanchors = [];

        foreach (PcrReanchor reanchor in Reanchors.Concat(joined))
        {
            reanchors[reanchor.Second] = reanchors.TryGetValue(reanchor.Second, out PcrReanchor? held)
                ? held with { After = reanchor.After }
                : reanchor;
        }

        return Rehydrate(anchor, [.. buckets.Values], [.. reanchors.Values]);
    }

    private int LastNamedSecond
        => Math.Max(Buckets.Count > 0 ? Buckets[^1].Second : 0, Reanchors.Count > 0 ? Reanchors[^1].Second : 0);

    private static long Wrapped(long pcr) => ((pcr % PcrWrapsAt) + PcrWrapsAt) % PcrWrapsAt;

    private static void WithinTheClock(long pcr, string parameterName)
    {
        if (pcr < 0 || pcr >= PcrWrapsAt)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pcr,
                $"A programme clock reference counts from 0 to {PcrWrapsAt - 1} and then starts again.");
        }
    }
}
