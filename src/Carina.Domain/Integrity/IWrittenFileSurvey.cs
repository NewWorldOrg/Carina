using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

/// <summary>
/// The places this process writes into itself — the roots it encodes into and the directory it draws
/// thumbnails into — which a sweep walks beside the recording roots.
/// </summary>
public interface IWrittenFileSurvey
{
    IReadOnlyList<OutputRoot> Places { get; }

    Task<RootListing> ListAsync(OutputRoot place, CancellationToken cancellationToken);

    /// <summary>
    /// Everything the ledgers claim, as the sweep has to see it: <paramref name="declared"/>, the thumbnail
    /// of every recording in <paramref name="ledger"/>, and each of those again under every walked place
    /// whose directory holds it, so a claim follows the file whichever name the place it lies in is walked
    /// under.
    /// </summary>
    IReadOnlyList<DeclaredFile> Claimed(IReadOnlyList<LedgerFile> ledger, IReadOnlyList<DeclaredFile> declared);
}
