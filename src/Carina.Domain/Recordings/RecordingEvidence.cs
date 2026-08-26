using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingEvidence
{
    public RecordingEvidence(
        long? fileSizeBytes,
        TimeSpan written,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime? abortedAt)
    {
        if (fileSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSizeBytes),
                fileSizeBytes,
                "A file is not smaller than empty.");
        }

        if (written < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(written), written, "A recording writes forwards.");
        }

        if (windowEnd <= windowStart)
        {
            throw new ArgumentException(
                "A recording window ends after it starts, and the ledger holds no recording whose window does not.",
                nameof(windowEnd));
        }

        FileSizeBytes = fileSizeBytes;
        Written = written;
        WindowStart = UtcTimes.Required(windowStart, nameof(windowStart));
        WindowEnd = UtcTimes.Required(windowEnd, nameof(windowEnd));
        AbortedAt = UtcTimes.Optional(abortedAt, nameof(abortedAt));
    }

    public long? FileSizeBytes { get; }

    public TimeSpan Written { get; }

    public DateTime WindowStart { get; }

    public DateTime WindowEnd { get; }

    public DateTime? AbortedAt { get; }

    public TimeSpan Window => WindowEnd - WindowStart;
}
