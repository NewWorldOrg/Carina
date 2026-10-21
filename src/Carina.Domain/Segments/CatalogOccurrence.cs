using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of one recording where the sound of a catalog entry is heard, in the recording's own time,
/// with the station, series and start of the recording copied from the record of its learning data, so
/// that it outlives the recording. It names the recording by value. <see cref="KindOverride"/> is what
/// this stretch alone is taken for when it differs from its entry.
/// </summary>
public sealed class CatalogOccurrence
{
    private string? seriesReadAgain;

    private CatalogOccurrence()
    {
    }

    public long Id { get; private set; }

    public CatalogEntryId EntryId { get; private set; } = null!;

    public RecordingId RecordingId { get; private set; } = null!;

    public TimeSpan StartsAt { get; private set; }

    public TimeSpan EndsAt { get; private set; }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public string? SeriesName { get; private set; }

    public DateTime RecordingStartedAt { get; private set; }

    public float BitErrorRate { get; private set; }

    public SegmentKind? KindOverride { get; private set; }

    public DateTime MatchedAt { get; private set; }

    public TimeSpan Length => EndsAt - StartsAt;

    public SoundSpan Span => new(StartsAt, EndsAt);

    /// <summary>
    /// What says which series the stretch's recording is of, with the series name kept read again
    /// (<see cref="SeriesNames.Of"/>), so that a stretch kept before a rule of series names changed is of the
    /// series a stretch found now is of.
    /// </summary>
    public SeriesMember Member => new(RecordingId, NetworkId, ServiceId, SeriesReadAgain(), RecordingStartedAt);

    /// <summary>
    /// The stretch found at <paramref name="span"/> of the recording, not yet kept, with the series name of
    /// the copy made again by today's rules; a copy kept before series names were made has one made from its
    /// programme's name.
    /// </summary>
    public static CatalogOccurrence Found(
        CatalogEntryId entryId,
        RecordingId recordingId,
        SoundSpan span,
        ProgrammeCopy programme,
        double bitErrorRate,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(programme);

        return Made(
            0,
            entryId,
            recordingId,
            span.Starts,
            span.Ends,
            programme.NetworkId,
            programme.ServiceId,
            SeriesNames.Of(programme.SeriesName ?? programme.Name),
            programme.RecordingStartedAt,
            (float)bitErrorRate,
            null,
            at);
    }

    public static CatalogOccurrence Rehydrate(
        long id,
        CatalogEntryId entryId,
        RecordingId recordingId,
        TimeSpan startsAt,
        TimeSpan endsAt,
        NetworkId networkId,
        ServiceId serviceId,
        string? seriesName,
        DateTime recordingStartedAt,
        float bitErrorRate,
        SegmentKind? kindOverride,
        DateTime matchedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);

        return Made(id, entryId, recordingId, startsAt, endsAt, networkId, serviceId, seriesName, recordingStartedAt, bitErrorRate, kindOverride, matchedAt);
    }

    public void Override(SegmentKind? kind)
    {
        if (kind is SegmentKind taken && !Enum.IsDefined(taken))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A stretch is taken for one of the kinds of segment.");
        }

        KindOverride = kind;
    }

    private string? SeriesReadAgain() => SeriesName is null ? null : (seriesReadAgain ??= SeriesNames.Of(SeriesName));

    private static CatalogOccurrence Made(
        long id,
        CatalogEntryId entryId,
        RecordingId recordingId,
        TimeSpan startsAt,
        TimeSpan endsAt,
        NetworkId networkId,
        ServiceId serviceId,
        string? seriesName,
        DateTime recordingStartedAt,
        float bitErrorRate,
        SegmentKind? kindOverride,
        DateTime matchedAt)
    {
        ArgumentNullException.ThrowIfNull(entryId);
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        if (startsAt < TimeSpan.Zero || endsAt <= startsAt)
        {
            throw new ArgumentException("A stretch starts no earlier than its recording's own zero and ends after it starts.", nameof(endsAt));
        }

        if (seriesName is { Length: > Reservation.NameMaxLength })
        {
            throw new ArgumentException($"A series name is kept in at most {Reservation.NameMaxLength} characters.", nameof(seriesName));
        }

        if (!(bitErrorRate is >= 0 and <= (float)SegmentMatcher.MostBitErrors))
        {
            throw new ArgumentOutOfRangeException(nameof(bitErrorRate), bitErrorRate, "A stretch disagrees with its entry in no more than three tenths of its bits.");
        }

        CatalogOccurrence occurrence = new()
        {
            Id = id,
            EntryId = entryId,
            RecordingId = recordingId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            NetworkId = networkId,
            ServiceId = serviceId,
            SeriesName = seriesName,
            RecordingStartedAt = UtcTimes.Required(recordingStartedAt, nameof(recordingStartedAt)),
            BitErrorRate = bitErrorRate,
            MatchedAt = UtcTimes.Required(matchedAt, nameof(matchedAt)),
        };

        occurrence.Override(kindOverride);

        return occurrence;
    }
}
