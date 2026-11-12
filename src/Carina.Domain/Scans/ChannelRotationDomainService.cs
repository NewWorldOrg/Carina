using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

/// <summary>
/// Records on each candidate channel held what a scan learnt of its reception, and which candidates that took out of
/// the rotation.
/// </summary>
public sealed class ChannelRotationDomainService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates)
{
    /// <summary>
    /// A candidate whose channel the scan walked is received when the attempt succeeded and the stream it reached
    /// names the candidate's service, and fails otherwise; an attempt that says nothing of reception leaves it alone.
    /// A candidate the failure takes out of the rotation is returned as a departure.
    /// </summary>
    public async Task<IReadOnlyList<RotationDeparture>> RecordScanAsync(
        IReadOnlyList<ScanRunAttempt> attempts,
        IReadOnlyDictionary<TuningParameters, IReadOnlySet<ServiceId>> named,
        RotationBackoff backoff,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(backoff);

        Dictionary<TuningParameters, ScanRunAttempt> walked = attempts.ToDictionary(attempt => attempt.Tuning, attempt => attempt);
        List<RotationDeparture> departures = [];

        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                cancellationToken);

            foreach (CandidateChannel candidate in stored)
            {
                if (!walked.TryGetValue(candidate.Tuning, out ScanRunAttempt? attempt) || attempt.SaysNothingOfReception)
                {
                    continue;
                }

                if (Received(attempt, named, service.ServiceId))
                {
                    candidate.RecordTuningSuccess(attempt.Measurement ?? SignalMeasurement.WithLock(at), at);
                }
                else if (Departs(candidate, backoff, at) is { } departure)
                {
                    departures.Add(departure);
                }

                await candidates.SaveAsync(candidate, cancellationToken);
            }
        }

        return departures;
    }

    private static bool Received(
        ScanRunAttempt attempt,
        IReadOnlyDictionary<TuningParameters, IReadOnlySet<ServiceId>> named,
        ServiceId serviceId)
        => attempt.Outcome is ScanAttemptOutcome.Succeeded
           && named.TryGetValue(attempt.Tuning, out IReadOnlySet<ServiceId>? carried)
           && carried.Contains(serviceId);

    private static RotationDeparture? Departs(CandidateChannel candidate, RotationBackoff backoff, DateTime at)
    {
        bool wasInRotation = candidate.IsInRotation;

        candidate.RecordTuningFailure(backoff, at);

        if (!wasInRotation || candidate.IsInRotation)
        {
            return null;
        }

        return new RotationDeparture(
            candidate.NetworkId,
            candidate.ServiceId,
            candidate.Tuning,
            candidate.ConsecutiveFailures,
            candidate.NeedsAttentionSince ?? at);
    }
}
