using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public interface IRecordingFileSurvey
{
    Task<IReadOnlyList<OutputRoot>> RootsAsync(CancellationToken cancellationToken);

    Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken);
}
