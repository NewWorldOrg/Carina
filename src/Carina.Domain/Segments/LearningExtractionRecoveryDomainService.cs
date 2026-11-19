using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed class LearningExtractionRecoveryDomainService(ILearningWorklist worklist)
{
    public async Task<bool> PutsBackAsync(LearningExtraction left, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(left);

        return await worklist.StandingAsync(left.RecordingId, cancellationToken) is not RecordingStanding.InFlight;
    }
}
