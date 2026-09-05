using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public enum EncodeFailure
{
    FfmpegExitedNonZero = 1,

    NotEnoughRoom = 2,

    SourceMissing = 3,

    CapabilityUnavailable = 4,

    TimedOut = 5,

    DestinationCollision = 6,

    HeadTooFar = 7,
}

public static class EncodeFailures
{
    public static EncodeFailure Named(EncodeFailure failure)
        => Enum.IsDefined(failure)
            ? failure
            : throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A job fails for one of the reasons the ledger holds.");
}

public static class EncodeNote
{
    public const int Longest = ProgrammeNote.Longest;

    public static string Of(string said) => ProgrammeNote.Of(said, Longest);
}

public sealed record EncodeFailureDetail
{
    public EncodeFailureDetail(EncodeFailure failure, string note, DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(note);

        Failure = EncodeFailures.Named(failure);
        Note = EncodeNote.Of(note);
        NoticedAt = UtcTimes.Required(noticedAt, nameof(noticedAt));
    }

    public EncodeFailure Failure { get; }

    public string Note { get; }

    public DateTime NoticedAt { get; }
}
