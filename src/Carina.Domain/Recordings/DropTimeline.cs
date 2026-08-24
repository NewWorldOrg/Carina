namespace Carina.Domain.Recordings;

public sealed record DropBucket(int Second, long Continuity, long Scrambled);

public sealed record PcrReanchor(int Second, long Before, long After);

public sealed record DropTimeline
{
    public const long PcrWrapsAt = 8_589_934_592;

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
