using Carina.Domain.Channels;
using Carina.Infrastructure.Scanning;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed class CandidateTuneFailureReporter(
    ICandidateChannelRepository candidates,
    ScanSettings settings,
    ILogger<CandidateTuneFailureReporter> logger) : ITuneFailureReporter
{
    public Task ReportFailureAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
        => TellAsync(
            candidateChannelId,
            candidate => candidate.RecordTuningFailure(settings.Rotation, at),
            cancellationToken);

    public Task ReportReachedAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
        => TellAsync(
            candidateChannelId,
            candidate => candidate.RecordTuningSuccess(SignalMeasurement.WithLock(at), at),
            cancellationToken);

    private async Task TellAsync(
        CandidateChannelId candidateChannelId,
        Action<CandidateChannel> tell,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateChannelId);

        CandidateChannel? candidate = await candidates.FindAsync(candidateChannelId, cancellationToken);

        if (candidate is null)
        {
            logger.LogDebug(
                "The candidate a collection visit tuned with is gone; there is nobody to tell.");

            return;
        }

        tell(candidate);

        await candidates.SaveAsync(candidate, cancellationToken);
    }
}
