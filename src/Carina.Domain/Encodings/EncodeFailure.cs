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
    public const int Longest = 1000;

    public static string Of(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        string kept = said.Trim();

        return kept.Length <= Longest ? kept : kept[^Longest..];
    }
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
