namespace Carina.Domain.Encodings;

public enum ArtefactClaim
{
    Claimed = 1,

    TakenByAnother = 2,
}

public interface IEncodeJobRepository
{
    Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken);

    Task AddAsync(EncodeJob job, CancellationToken cancellationToken);

    Task SaveAsync(EncodeJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the job's artefact name into the ledger before anything is renamed. The ledger holds
    /// one owner per name under a root, so the answer is the claim or the news that another job
    /// already holds that name; the job itself is saved as it stands either way (BR-ED2-009).
    /// </summary>
    Task<ArtefactClaim> ClaimArtefactAsync(EncodeJob job, EncodeFileName name, CancellationToken cancellationToken);
}
