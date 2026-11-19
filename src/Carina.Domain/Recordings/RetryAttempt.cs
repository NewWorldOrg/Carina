using Carina.Domain.Channels;

namespace Carina.Domain.Recordings;

public enum RetryResult
{
    Started = 1,

    RefusedAgain = 2,

    NoAnswer = 3,
}

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

    public static RetryAttempt Of(bool started, bool refusedByTheDriver, RecordingStartFailure? named)
    {
        if (started)
        {
            return Started;
        }

        return refusedByTheDriver ? RefusedAgain(named) : Unanswered;
    }

    public static RetryAttempt RefusedAgain(RecordingStartFailure? named)
        => named is null
            ? new RetryAttempt(RetryResult.RefusedAgain, null, [])
            : new RetryAttempt(RetryResult.RefusedAgain, named.TuneFailure, [named.Fault]);
}
