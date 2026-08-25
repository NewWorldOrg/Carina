using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingEvidence
{
    public RecordingEvidence(
        long? fileSizeBytes,
        TimeSpan? written,
        DateTime? windowStart,
        DateTime? windowEnd,
        DateTime? abortedAt)
    {
        if (fileSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeBytes),
                fileSizeBytes,
                "A file is not smaller than empty.");
        }

        if (written is { } length && length < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(written), written, "A recording writes forwards.");
        }

        FileSizeBytes = fileSizeBytes;
        Written = written;
        WindowStart = UtcTimes.Optional(windowStart, nameof(windowStart));
        WindowEnd = UtcTimes.Optional(windowEnd, nameof(windowEnd));
        AbortedAt = UtcTimes.Optional(abortedAt, nameof(abortedAt));
    }

    public long? FileSizeBytes { get; }

    public TimeSpan? Written { get; }

    public DateTime? WindowStart { get; }

    public DateTime? WindowEnd { get; }

    public DateTime? AbortedAt { get; }
}
