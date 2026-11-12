using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Decides, after a start, what becomes of a record a follow left following when the process last stopped, looking
/// up whether its recording is still being written: while it is, the record is left for the follow to take up again
/// from the head; once it has ended or gone, the record is put back to wait to be read.
/// </summary>
public sealed class LearningExtractionRecoveryDomainService(ILearningWorklist worklist)
{
    public async Task<bool> PutsBackAsync(LearningExtraction left, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(left);

        return await worklist.StandingAsync(left.RecordingId, cancellationToken) is not RecordingStanding.InFlight;
    }
}
