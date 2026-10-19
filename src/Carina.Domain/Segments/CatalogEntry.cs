using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// A sound heard more than once, kept in the catalog by a representative fingerprint, with the kind it
/// is taken for and the counts that kind was estimated from: its stretches, and the series and stations
/// its whole stretches are spread over (<see cref="CatalogKinds"/>), and the version of the estimation it
/// was last estimated with. An entry is started once two stretches of it are found, is estimated again
/// whenever stretches are added and once more when the estimation changes, and is never removed.
/// </summary>
public sealed class CatalogEntry
{
    public const int MostFrames = SegmentMatcher.MostFrames;

    internal const int FewestOccurrences = 2;

    private CatalogEntry()
    {
    }

    public CatalogEntryId Id { get; private set; } = null!;

    public CatalogKind Kind { get; private set; }

    public double? Confidence { get; private set; }

    public CatalogDecision? DecidedBy { get; private set; }

    public TimeSpan Length { get; private set; }

    public byte[] Fingerprint { get; private set; } = [];

    public int OccurrenceCount { get; private set; }

    public int SeriesCount { get; private set; }

    public int StationCount { get; private set; }

    public DateTime FirstSeenAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public int KindsVersion { get; private set; }

    /// <summary>
    /// Whether the entry was last estimated with an earlier version of the estimation than
    /// <see cref="CatalogKinds.Version"/>, and waits to be estimated again.
    /// </summary>
    public bool AwaitsReestimation => KindsVersion < CatalogKinds.Version;

    /// <summary>
    /// The entry of the sound whose fingerprint is <paramref name="frames"/>, estimated from its first
    /// stretches.
    /// </summary>
    public static CatalogEntry Start(
        CatalogEntryId id,
        ReadOnlySpan<uint> frames,
        IReadOnlyList<PlacedOccurrence> occurrences,
        SeriesLinks links,
        DateTime at)
    {
        if (frames.Length is < 1 or > MostFrames)
        {
            throw new ArgumentException($"A fingerprint holds 1 to {MostFrames} frames.", nameof(frames));
        }

        CatalogEntry entry = new()
        {
            Id = id ?? throw new ArgumentNullException(nameof(id)),
            Length = SegmentMatcher.Frame * frames.Length,
            Fingerprint = CatalogFingerprint.Write(frames),
        };

        entry.Reestimate(occurrences, links, at);

        return entry;
    }

    public static CatalogEntry Rehydrate(
        CatalogEntryId id,
        CatalogKind kind,
        double? confidence,
        CatalogDecision? decidedBy,
        TimeSpan length,
        byte[] fingerprint,
        int occurrenceCount,
        int seriesCount,
        int stationCount,
        DateTime firstSeenAt,
        DateTime lastSeenAt,
        DateTime updatedAt,
        int kindsVersion)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(kindsVersion, CatalogKinds.Unsettled);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrenceCount, FewestOccurrences);
        ArgumentOutOfRangeException.ThrowIfLessThan(seriesCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stationCount, 1);

        if (fingerprint.Length is 0 || fingerprint.Length % sizeof(uint) is not 0 || fingerprint.Length > MostFrames * sizeof(uint))
        {
            throw new ArgumentException($"A fingerprint holds 1 to {MostFrames} frames of four bytes each.", nameof(fingerprint));
        }

        CatalogEntry entry = new()
        {
            Id = id,
            Length = length,
            Fingerprint = fingerprint,
        };

        entry.Set(new CatalogEstimate(kind, confidence, decidedBy), occurrenceCount, seriesCount, stationCount, firstSeenAt, lastSeenAt, updatedAt);
        entry.KindsVersion = kindsVersion;

        return entry;
    }

    public uint[] Frames() => CatalogFingerprint.Read(Fingerprint);

    /// <summary>
    /// Estimates the entry again from every one of its stretches with <see cref="CatalogKinds.Version"/> of
    /// the estimation, and gives each stretch what it alone is taken for.
    /// </summary>
    public void Reestimate(IReadOnlyList<PlacedOccurrence> occurrences, SeriesLinks links, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(links);

        if (occurrences.Count < FewestOccurrences || occurrences.Any(placed => !placed.Occurrence.EntryId.Equals(Id)))
        {
            throw new ArgumentException($"An entry is estimated from at least {FewestOccurrences} stretches, all of its own.", nameof(occurrences));
        }

        CatalogPattern pattern = CatalogKinds.Estimate(Length, occurrences, links);
        DateTime[] seen = [.. occurrences.Select(placed => placed.Occurrence.RecordingStartedAt)];

        Set(pattern.Estimate, occurrences.Count, pattern.SeriesCount, pattern.StationCount, seen.Min(), seen.Max(), at);
        KindsVersion = CatalogKinds.Version;

        for (int each = 0; each < occurrences.Count; each++)
        {
            occurrences[each].Occurrence.Override(pattern.Overrides[each]);
        }
    }

    private void Set(
        CatalogEstimate estimate,
        int occurrenceCount,
        int seriesCount,
        int stationCount,
        DateTime firstSeenAt,
        DateTime lastSeenAt,
        DateTime updatedAt)
    {
        if (!Enum.IsDefined(estimate.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(estimate), estimate.Kind, "An entry is taken for one of the kinds the catalog knows.");
        }

        if (estimate.DecidedBy is CatalogDecision decision && !Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(estimate), decision, "An entry's kind is decided by its pattern.");
        }

        if (estimate.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(estimate), estimate.Confidence, "A confidence is 0 to 1.");
        }

        bool unknown = estimate.Kind is CatalogKind.Unknown;

        if (unknown != (estimate.Confidence is null) || unknown != (estimate.DecidedBy is null))
        {
            throw new ArgumentException("An entry not yet known has neither a confidence nor what decided it, and every other has both.", nameof(estimate));
        }

        DateTime first = UtcTimes.Required(firstSeenAt, nameof(firstSeenAt));
        DateTime last = UtcTimes.Required(lastSeenAt, nameof(lastSeenAt));

        if (last < first)
        {
            throw new ArgumentException("An entry is last seen no earlier than it was first seen.", nameof(lastSeenAt));
        }

        Kind = estimate.Kind;
        Confidence = estimate.Confidence;
        DecidedBy = estimate.DecidedBy;
        OccurrenceCount = occurrenceCount;
        SeriesCount = seriesCount;
        StationCount = stationCount;
        FirstSeenAt = first;
        LastSeenAt = last;
        UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt));
    }
}
