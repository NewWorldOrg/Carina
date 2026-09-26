using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public enum ArtefactClaim
{
    Claimed = 1,

    TakenByAnother = 2,
}

/// <summary>
/// How an attempt to start the next job ended. <see cref="AViewerHoldsTheCard"/> means the ledger
/// was not asked because the card was making a picture for someone watching; the others are the
/// ledger's answer to a claim.
/// </summary>
public enum EncodeClaimStanding
{
    Claimed = 1,

    NothingWaiting = 2,

    AnotherIsRunning = 3,

    TakenMeanwhile = 4,

    AViewerHoldsTheCard = 5,
}

/// <summary>
/// What a look at the queue came back with. The job is there only when this caller now holds it as
/// running; the other answers say why not.
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

public sealed record EncodeHold(bool Any, EncodeJob? Unfinished);

public interface IEncodeJobRepository
{
    Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken);

    Task AddAsync(EncodeJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the job as it stands.
    /// </summary>
    /// <exception cref="EncodeJobMovedMeanwhileException">The row changed since it was read.</exception>
    Task SaveAsync(EncodeJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a job that has ended over its row, without having read that row, when the ledger still
    /// holds it as running on the same attempt.
    /// </summary>
    /// <returns><see langword="false"/> when the row has moved on and was left as it was.</returns>
    Task<bool> WriteTheEndingAsync(EncodeJob job, CancellationToken cancellationToken);

    Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListForRecordingAsync(RecordingId recordingId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the oldest waiting job to running by a conditional update, and hands it back only when
    /// that update changed one row. The ledger holds at most one running job, so a second claim while
    /// one runs is refused by the ledger.
    /// </summary>
    Task<EncodeClaim> ClaimNextAsync(DateTime at, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListRunningAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the job's artefact name into the ledger before anything is renamed, and saves the job as
    /// it stands. The ledger holds one owner per name under a root.
    /// </summary>
    /// <returns>The claim, or that another job already holds the name.</returns>
    Task<ArtefactClaim> ClaimArtefactAsync(EncodeJob job, EncodeFileName name, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the artefact name over from whichever earlier job still holds it under this root, for a job
    /// asked to make the artefact again. The earlier row keeps what it made and is marked as having
    /// given the name up; a job not asked to make it again is refused.
    /// </summary>
    /// <returns>How many rows gave the name up.</returns>
    Task<int> TakeTheNameOverAsync(EncodeJob job, EncodeFileName name, DateTime at, CancellationToken cancellationToken);

    /// <summary>
    /// How long the last jobs that completed took, at most <paramref name="most"/> of them, newest
    /// first. Jobs that failed or were called off are not counted.
    /// </summary>
    Task<IReadOnlyList<EncodeSpell>> RecentSpellsAsync(int most, CancellationToken cancellationToken);

    Task<EncodeHold> HoldOnProfileAsync(EncodeProfileId profileId, CancellationToken cancellationToken);

    Task<EncodeHold> HoldOnDestinationAsync(EncodeDestinationId destinationId, CancellationToken cancellationToken);
}
