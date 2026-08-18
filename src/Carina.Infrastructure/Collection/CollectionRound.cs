using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

public sealed record RoundResult(int Visited, int Gathered, int CameBackShort);

public sealed class CollectionRound(
    IStreamVisitRepository visits,
    IProgrammeRepository programmes,
    StreamVisitor visitor,
    CollectionSettings settings,
    TimeProvider clock)
{
    public async Task<RoundResult> WalkAsync(
        IReadOnlyList<StreamToVisit> streams,
        CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(streams);

        var now = clock.GetUtcNow().UtcDateTime;
        var plan = CollectionPlan.Of(await CoverageAsync(streams, abort), now, settings.WantedCoverage);
        var visited = 0;
        var gathered = 0;
        var cameBackShort = 0;

        foreach (var planned in plan)
        {
            abort.ThrowIfCancellationRequested();

            if (streams.FirstOrDefault(stream =>
                stream.NetworkId.Equals(planned.NetworkId)
                && stream.TransportStreamId.Equals(planned.TransportStreamId)) is not { } stream)
            {
                continue;
            }

            var began = clock.GetTimestamp();
            var visit = await visitor.VisitAsync(stream.Tuning, hurried: false, abort);

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

    private async Task RecordAsync(
        StreamToVisit stream,
        VisitResult visit,
        TimeSpan took,
        CancellationToken abort)
    {
        var at = clock.GetUtcNow().UtcDateTime;
        var held = await visits.FindAsync(stream.NetworkId, stream.TransportStreamId, abort);

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
        IReadOnlyList<StreamToVisit> streams,
        CancellationToken abort)
    {
        var known = await visits.ListAsync(abort);
        var coverage = new List<StreamCoverage>(streams.Count);

        foreach (var stream in streams)
        {
            var visit = known.FirstOrDefault(candidate =>
                candidate.NetworkId.Equals(stream.NetworkId)
                && candidate.TransportStreamId.Equals(stream.TransportStreamId));
            var everGathered = visit?.LastCompletedAt is not null;
            var services = new List<ServiceCoverage>(stream.Services.Count);

            foreach (var service in stream.Services)
            {
                var until = await programmes.CoveredUntilAsync(
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

public sealed record StreamToVisit(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    TuningParameters Tuning,
    IReadOnlyList<ServiceId> Services);
