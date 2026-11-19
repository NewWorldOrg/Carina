using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public enum ExtractionFailure
{
    FfmpegMissing = 1,

    StreamMissing = 2,

    TimingMismatch = 3,

    Other = 4,
}

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
