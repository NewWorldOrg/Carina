using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public interface IWrittenFileSurvey
{
    IReadOnlyList<OutputRoot> Places { get; }

    Task<RootListing> ListAsync(OutputRoot place, CancellationToken cancellationToken);

    IReadOnlyList<DeclaredFile> Claimed(IReadOnlyList<LedgerFile> ledger, IReadOnlyList<DeclaredFile> declared);

    IReadOnlyList<DrawnPicture> Drawn(IReadOnlyList<LedgerFile> ledger);
}
