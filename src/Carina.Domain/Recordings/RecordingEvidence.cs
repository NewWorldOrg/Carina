using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingEvidence
{
    public RecordingEvidence(
        long? fileSizeBytes,
        TimeSpan written,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime? abortedAt,
        QualityLevel leftScrambled = QualityLevel.Unmeasured)
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
        LeftScrambled = Enum.IsDefined(leftScrambled)
            ? leftScrambled
            : throw new ArgumentOutOfRangeException(
                nameof(leftScrambled),
                leftScrambled,
                "What was left scrambled is read at one of the four levels.");
    }

    public long? FileSizeBytes { get; }

    public TimeSpan Written { get; }

    public DateTime WindowStart { get; }

    public DateTime WindowEnd { get; }

    public DateTime? AbortedAt { get; }

    public QualityLevel LeftScrambled { get; }

    public TimeSpan Window => WindowEnd - WindowStart;
}
