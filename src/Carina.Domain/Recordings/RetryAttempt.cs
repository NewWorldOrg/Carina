using Carina.Domain.Channels;

namespace Carina.Domain.Recordings;

public enum RetryResult
{
    Started = 1,

    RefusedAgain = 2,

    NoAnswer = 3,
}

/// <summary>
/// What came of one attempt to start a recording again, in the classes the ledger already holds. A
/// refusal that named a class carries that class; one that named none carries nothing rather than a
/// class it was not given; and an attempt nothing answered is kept apart from both, because "the
/// driver said no" and "nobody said anything" are different facts.
/// </summary>
public sealed record RetryAttempt
{
    private RetryAttempt(RetryResult result, TuneFailureKind? tuneFailure, IReadOnlyList<RecordingFault> faults)
    {
        Result = result;
        TuneFailure = tuneFailure;
        Faults = faults;
    }

    public static RetryAttempt Started { get; } = new(RetryResult.Started, null, []);

    public static RetryAttempt Unanswered { get; } = new(RetryResult.NoAnswer, null, []);

    public RetryResult Result { get; }

    public TuneFailureKind? TuneFailure { get; }

    public IReadOnlyList<RecordingFault> Faults { get; }

    public static RetryAttempt RefusedAgain(RecordingStartFailure? named)
        => named is null
            ? new RetryAttempt(RetryResult.RefusedAgain, null, [])
            : new RetryAttempt(RetryResult.RefusedAgain, named.TuneFailure, [named.Fault]);
}
