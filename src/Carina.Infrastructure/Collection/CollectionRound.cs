using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record RoundResult(int Visited, int Gathered, int CameBackShort);

public sealed class CollectionRound(
    IStreamVisitRepository visits,
    IProgrammeRepository programmes,
    StreamVisitor visitor,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<CollectionRound> logger)
{
    public async Task<RoundResult> WalkAsync(
        IReadOnlyList<BroadcastStream> streams,
        CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(streams);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<PlannedVisit> plan = CollectionPlan.Of(await CoverageAsync(streams, abort), now, settings.WantedCoverage);
        int visited = 0;
        int gathered = 0;
        int cameBackShort = 0;

        foreach (PlannedVisit planned in plan)
        {
            abort.ThrowIfCancellationRequested();

            if (streams.FirstOrDefault(stream =>
                stream.NetworkId.Equals(planned.NetworkId)
                && stream.TransportStreamId.Equals(planned.TransportStreamId)) is not { } stream)
            {
                continue;
            }

            long began = clock.GetTimestamp();
            VisitResult visit = await VisitAsync(stream, abort);

            if (visit.WorthWaitingOut)
            {
                logger.LogInformation(
                    "Every tuner stayed busy; the rest of this walk waits for the next sweep.");
                await RecordAsync(stream, visit, clock.GetElapsedTime(began), abort);

                break;
            }

            visited++;

            if (visit.Outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly)
            {
                gathered++;
            }
            else if (visit.Outcome is not VisitOutcome.Interrupted)
            {
                cameBackShort++;
            }

            await RecordAsync(stream, visit, clock.GetElapsedTime(began), abort);
        }

        return new RoundResult(visited, gathered, cameBackShort);
    }

    private async Task<VisitResult> VisitAsync(BroadcastStream stream, CancellationToken abort)
    {
        int refusals = 0;

        while (true)
        {
            VisitResult visit;

            try
            {
                visit = await visitor.VisitAsync(stream.Tuning, hurried: false, abort);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger.LogWarning(
                    failure,
                    "Visiting {NetworkId}-{TransportStreamId} failed; the walk carries on.",
                    stream.NetworkId.Value,
                    stream.TransportStreamId.Value);

                return new VisitResult(VisitOutcome.Interrupted, new ProgrammesWritten(0, 0, 0), failure.Message);
            }

            if (!visit.WorthWaitingOut)
            {
                return visit;
            }

            refusals++;

            if (refusals >= settings.WhenTunersAreFull.FailureCeiling)
            {
                return visit;
            }

            await Task.Delay(settings.WhenTunersAreFull.DelayAfter(refusals), clock, abort);
        }
    }

    private async Task RecordAsync(
        BroadcastStream stream,
        VisitResult visit,
        TimeSpan took,
        CancellationToken abort)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        StreamVisit? held = await visits.FindAsync(stream.NetworkId, stream.TransportStreamId, abort);

        if (held is null)
        {
            await visits.SaveAsync(
                StreamVisit.Record(stream.NetworkId, stream.TransportStreamId, visit.Outcome, at, took),
                abort);

            return;
        }

        held.Record(visit.Outcome, at, took);

        await visits.SaveAsync(held, abort);
    }

    private async Task<IReadOnlyList<StreamCoverage>> CoverageAsync(
        IReadOnlyList<BroadcastStream> streams,
        CancellationToken abort)
    {
        IReadOnlyList<StreamVisit> known = await visits.ListAsync(abort);
        var coverage = new List<StreamCoverage>(streams.Count);

        foreach (BroadcastStream stream in streams)
        {
            StreamVisit? visit = known.FirstOrDefault(candidate =>
                candidate.NetworkId.Equals(stream.NetworkId)
                && candidate.TransportStreamId.Equals(stream.TransportStreamId));
            bool everGathered = visit?.LastCompletedAt is not null;
            var services = new List<ServiceCoverage>(stream.Services.Count);

            foreach (ServiceId service in stream.Services)
            {
                DateTime? until = await programmes.CoveredUntilAsync(
                    stream.NetworkId.Value,
                    service.Value,
                    abort);

                services.Add(new ServiceCoverage(service, until, everGathered));
            }

            coverage.Add(new StreamCoverage(
                stream.NetworkId,
                stream.TransportStreamId,
                services,
                visit?.LastCompletedAt,
                visit is null ? null : CollectionBackOff.NotBefore(visit, settings)));
        }

        return coverage;
    }
}
