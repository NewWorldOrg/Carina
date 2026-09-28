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
    /// The thumbnail of every recording the ledger holds, as a file the sweep does not call an orphan.
    /// </summary>
    IReadOnlyList<DeclaredFile> PicturesOf(IReadOnlyList<LedgerFile> ledger);
}
