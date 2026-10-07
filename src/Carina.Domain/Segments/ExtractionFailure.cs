using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// Why the learning data could not be taken out of a recording.
/// </summary>
public enum ExtractionFailure
{
    FfmpegMissing = 1,

    StreamMissing = 2,

    TimingMismatch = 3,

    Other = 4,
}

/// <summary>
/// The kind of a failure to take the learning data out of a recording, and a short reason.
/// </summary>
public sealed record ExtractionFailureDetail
{
    public const int LongestReason = 500;

    public ExtractionFailureDetail(ExtractionFailure failure, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "A failure is one of the four kinds kept.");
        }

        Failure = failure;
        Reason = ProgrammeNote.Of(reason, LongestReason);
    }

    public ExtractionFailure Failure { get; }

    public string Reason { get; }
}
