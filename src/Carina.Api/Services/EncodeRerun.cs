using Carina.Domain.Encodings;

namespace Carina.Api.Services;

/// <summary>
/// What became of one id handed in to be run again: a job was queued in its place, or none was, for the
/// reason named.
/// </summary>
public enum EncodeRerunOutcome
{
    Queued = 1,

    NoSuchJob = 2,

    NotFailed = 3,

    AlreadyInTheQueue = 4,

    AlreadyEncoded = 5,

    Refused = 6,
}

/// <summary>
/// One row of the answer to running failed jobs again: the id as it was asked, what became of it, why
/// when nothing was queued, and the job queued in its place when one was.
/// </summary>
public sealed record EncodeRerunRow(EncodeJobId Id, EncodeRerunOutcome Outcome, string Message, EncodeJobView? Job)
{
    public static EncodeRerunRow QueuedAgain(EncodeJobId id, EncodeJobView job)
        => new(id, EncodeRerunOutcome.Queued, string.Empty, job);

    public static EncodeRerunRow NoSuchJob(EncodeJobId id)
        => new(id, EncodeRerunOutcome.NoSuchJob, "The ledger holds no such job.", null);

    public static EncodeRerunRow NotFailed(EncodeJobId id, EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new EncodeRerunRow(
            id,
            EncodeRerunOutcome.NotFailed,
            $"The job stands at {job.Status}, and only a failed job is run again.",
            null);
    }

    public static EncodeRerunRow NotQueued(EncodeJobId id, string message, EncodingFailure failure)
        => new(id, EncodeReruns.Outcome(failure), message, null);
}

/// <summary>
/// The rules of running failed jobs again: how many are taken in one request, which jobs may be run
/// again, and how a refusal of the queue reads as an outcome of a row.
/// </summary>
public static class EncodeReruns
{
    public const int MostAtOnce = 100;

    public static readonly string Description = $"between one and {MostAtOnce} job ids.";

    public static bool CanRunAgain(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.Status is EncodeJobStatus.Failed;
    }

    public static EncodeRerunOutcome Outcome(EncodingFailure failure) => failure switch
    {
        EncodingFailure.NoSuchJob => EncodeRerunOutcome.NoSuchJob,
        EncodingFailure.AlreadyInTheQueue => EncodeRerunOutcome.AlreadyInTheQueue,
        EncodingFailure.AlreadyEncoded => EncodeRerunOutcome.AlreadyEncoded,
        _ => EncodeRerunOutcome.Refused,
    };
}
