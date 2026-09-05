using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public enum ArtefactClaim
{
    Claimed = 1,

    TakenByAnother = 2,
}

public enum EncodeClaimStanding
{
    Claimed = 1,

    NothingWaiting = 2,

    AnotherIsRunning = 3,

    TakenMeanwhile = 4,
}

/// <summary>
/// What a look at the queue came back with. The job is there only when this caller now holds it
/// as running; the other three answers say why not, so a caller can tell an empty queue from a
/// queue somebody else is working through.
/// </summary>
public sealed record EncodeClaim
{
    private EncodeClaim(EncodeJob? job, EncodeClaimStanding standing)
    {
        Job = job;
        Standing = standing;
    }

    public EncodeJob? Job { get; }

    public EncodeClaimStanding Standing { get; }

    public static EncodeClaim Of(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is not EncodeJobStatus.Running)
        {
            throw new ArgumentException("A claimed job is one the ledger now holds as running.", nameof(job));
        }

        return new EncodeClaim(job, EncodeClaimStanding.Claimed);
    }

    public static EncodeClaim NothingWaiting() => new(null, EncodeClaimStanding.NothingWaiting);

    public static EncodeClaim AnotherIsRunning() => new(null, EncodeClaimStanding.AnotherIsRunning);

    public static EncodeClaim TakenMeanwhile() => new(null, EncodeClaimStanding.TakenMeanwhile);
}

public interface IEncodeJobRepository
{
    Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken);

    Task AddAsync(EncodeJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the job as it stands. A row that moved under this hand since it was read — called off
    /// while it ran, as a rule — is not written over: the save throws
    /// <see cref="EncodeJobMovedMeanwhileException"/> and the ledger's word stands.
    /// </summary>
    Task SaveAsync(EncodeJob job, CancellationToken cancellationToken);

    Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListForRecordingAsync(RecordingId recordingId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the oldest waiting job to running by a conditional update, and hands it back only when
    /// that update changed one row. One running job is all the ledger holds, so a second claim while
    /// one runs is refused by the ledger itself, never by anything this process remembers (BR-ED2-005).
    /// </summary>
    Task<EncodeClaim> ClaimNextAsync(DateTime at, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListRunningAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the job's artefact name into the ledger before anything is renamed. The ledger holds
    /// one owner per name under a root, so the answer is the claim or the news that another job
    /// already holds that name; the job itself is saved as it stands either way (BR-ED2-009).
    /// </summary>
    Task<ArtefactClaim> ClaimArtefactAsync(EncodeJob job, EncodeFileName name, CancellationToken cancellationToken);
}
