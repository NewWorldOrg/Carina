using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Library;

public sealed record LibraryRecordingSummary
{
    private LibraryRecordingSummary(
        RecordingId id,
        NetworkId networkId,
        ServiceId serviceId,
        string name,
        DateTime startedAt,
        TimeSpan written,
        long? fileSizeObserved,
        DateTime? observedAt,
        RecordingOutcome outcome,
        QualityLevel quality,
        ThumbnailState thumbnailState,
        BroadcastGroupKey? broadcastGroupKey,
        BroadcastGroupRole broadcastGroupRole)
    {
        Id = id;
        NetworkId = networkId;
        ServiceId = serviceId;
        Name = name;
        StartedAt = startedAt;
        Written = written;
        FileSizeObserved = fileSizeObserved;
        ObservedAt = observedAt;
        Outcome = outcome;
        Quality = quality;
        ThumbnailState = thumbnailState;
        BroadcastGroupKey = broadcastGroupKey;
        BroadcastGroupRole = broadcastGroupRole;
    }

    public RecordingId Id { get; }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public string Name { get; }

    public DateTime StartedAt { get; }

    public TimeSpan Written { get; }

    public long? FileSizeObserved { get; }

    public DateTime? ObservedAt { get; }

    public RecordingOutcome Outcome { get; }

    public QualityLevel Quality { get; }

    public ThumbnailState ThumbnailState { get; }

    public BroadcastGroupKey? BroadcastGroupKey { get; }

    public BroadcastGroupRole BroadcastGroupRole { get; }

    public RecordingCursor Cursor => new(StartedAt, Id);

    public static LibraryRecordingSummary Of(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.Outcome is not { } outcome)
        {
            throw new ArgumentException(
                "A recording that is still being written is not in the library yet.",
                nameof(recording));
        }

        return new LibraryRecordingSummary(
            recording.Id,
            recording.NetworkId,
            recording.ServiceId,
            recording.SnapshotName,
            recording.StartedAtActual,
            recording.Written,
            recording.FileSizeObserved,
            recording.ObservedAt,
            outcome,
            RecordingQuality.Of(recording.Counters, recording.ScrambledPackets),
            recording.ThumbnailState,
            recording.BroadcastGroupKey,
            recording.BroadcastGroupRole);
    }
}
