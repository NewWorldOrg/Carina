using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed class CandidateTuneFailureReporter(
    ChannelRotationDomainService rotation,
    ScanSettings settings,
    ILogger<CandidateTuneFailureReporter> logger) : ITuneFailureReporter
{
    public async Task ReportFailureAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
        => Told(await rotation.RecordTuneFailureAsync(candidateChannelId, settings.Rotation, at, cancellationToken));

    public async Task ReportReachedAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
        => Told(await rotation.RecordTuneReachedAsync(candidateChannelId, at, cancellationToken));

    private void Told(bool told)
    {
        if (!told)
        {
            logger.LogDebug(
                "The candidate a collection visit tuned with is gone; there is nobody to tell.");
        }
    }
}
