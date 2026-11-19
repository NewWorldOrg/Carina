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

    AViewerHoldsTheCard = 5,
}

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

    Task SaveAsync(EncodeJob job, CancellationToken cancellationToken);

    Task<bool> WriteTheEndingAsync(EncodeJob job, CancellationToken cancellationToken);

    Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListForRecordingAsync(RecordingId recordingId, CancellationToken cancellationToken);

    Task<EncodeClaim> ClaimNextAsync(DateTime at, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeJob>> ListRunningAsync(CancellationToken cancellationToken);

    Task<ArtefactClaim> ClaimArtefactAsync(EncodeJob job, EncodeFileName name, CancellationToken cancellationToken);

    Task<int> TakeTheNameOverAsync(EncodeJob job, EncodeFileName name, DateTime at, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordingId>> ListRecordingsMadeMoreThanOnceAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeSpell>> RecentSpellsAsync(int most, CancellationToken cancellationToken);

    Task<EncodeHold> HoldOnProfileAsync(EncodeProfileId profileId, CancellationToken cancellationToken);

    Task<EncodeHold> HoldOnDestinationAsync(EncodeDestinationId destinationId, CancellationToken cancellationToken);
}
