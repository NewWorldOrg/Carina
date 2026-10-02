using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed record RoundResult(int Visited, int Gathered, int CameBackShort, int TurnedAway = 0);

public sealed class CollectionRound(
    IStreamVisitRepository visits,
    IProgrammeRepository programmes,
    StreamVisitor visitor,
    RescanNoticeBoard rescans,
    ITuneFailureReporter tuning,
    IAppEventPublisher events,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<CollectionRound> logger)
{
    /// <summary>
    /// Visits every stream that is due, in the order of the plan. A stream every tuner is too busy for is
    /// stepped over, and those stepped over are tried again together after each wait on full tuners.
    /// </summary>
    public async Task<RoundResult> WalkAsync(
        IReadOnlyList<BroadcastStream> streams,
        CancellationToken interruption,
        CancellationToken abort,
        bool hurried = false)
    {
        ArgumentNullException.ThrowIfNull(streams);

        using CancellationTokenSource walking = CancellationTokenSource.CreateLinkedTokenSource(
            interruption,
            abort);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<PlannedVisit> plan = CollectionPlan.Of(
            await CoverageAsync(streams, abort),
            now,
            settings.RevisitsBelow,
            hurried);
        IReadOnlyList<BroadcastStream> waiting =
        [
            .. plan
                .Select(planned => streams.FirstOrDefault(stream =>
                    stream.NetworkId.Equals(planned.NetworkId)
                    && stream.TransportStreamId.Equals(planned.TransportStreamId)))
                .OfType<BroadcastStream>(),
        ];
        var turnedAway = new List<TurnedAway>();
        var walked = new RoundResult(0, 0, 0);

        for (int attempt = 1; waiting.Count > 0; attempt++)
        {
            Pass pass = await PassAsync(waiting, turnedAway, hurried, walking.Token, abort);

            walked = new RoundResult(
                walked.Visited + pass.Visited,
                walked.Gathered + pass.Gathered,
                walked.CameBackShort + pass.CameBackShort);

            if (pass.TheDriverWentAway
                || turnedAway.Count == 0
                || attempt >= settings.WhenTunersAreFull.FailureCeiling
                || !await WaitedForATunerAsync(attempt, walking.Token, abort))
            {
                break;
            }

            waiting = [.. turnedAway.Select(refused => refused.Stream)];
        }

        foreach (TurnedAway refused in turnedAway)
        {
            await RecordAsync(refused.Stream, refused.Visit, refused.Took, abort);
        }

        if (turnedAway.Count > 0)
        {
            logger.LogInformation(
                "Every tuner stayed busy for {TurnedAway} stream(s); they wait for the next sweep.",
                turnedAway.Count);
        }

        return walked with { TurnedAway = turnedAway.Count };
    }

    private async Task<Pass> PassAsync(
        IReadOnlyList<BroadcastStream> waiting,
        List<TurnedAway> turnedAway,
        bool hurried,
        CancellationToken walking,
        CancellationToken abort)
    {
        int visited = 0;
        int gathered = 0;
        int cameBackShort = 0;

        foreach (BroadcastStream stream in waiting)
        {
            abort.ThrowIfCancellationRequested();

            long began = clock.GetTimestamp();
            VisitResult? visit = await VisitAsync(stream, hurried, began, walking, abort);

            turnedAway.RemoveAll(refused => ReferenceEquals(refused.Stream, stream));

            if (visit is null)
            {
                return new Pass(visited, gathered, cameBackShort, TheDriverWentAway: true);
            }

            if (visit.WorthWaitingOut)
            {
                turnedAway.Add(new TurnedAway(stream, visit, clock.GetElapsedTime(began)));

                continue;
            }

            visited++;
            gathered += visit.Outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly ? 1 : 0;
            cameBackShort += CameBackShort(visit.Outcome) ? 1 : 0;

            await SettleAsync(stream, visit, clock.GetElapsedTime(began), abort);
        }

        return new Pass(visited, gathered, cameBackShort, TheDriverWentAway: false);
    }

    private static bool CameBackShort(VisitOutcome outcome)
        => outcome is VisitOutcome.Incomplete or VisitOutcome.NoLock or VisitOutcome.NoBytes;

    private async Task SettleAsync(BroadcastStream stream, VisitResult visit, TimeSpan took, CancellationToken abort)
    {
        if (visit.Written.Discarded > 0 || visit.Written.Clamped > 0)
        {
            logger.LogInformation(
                "Visiting {NetworkId}-{TransportStreamId} threw away {Discarded} event(s) that could not be taken "
                + "and cut {Clamped} programme(s) to the length kept.",
                stream.NetworkId.Value,
                stream.TransportStreamId.Value,
                visit.Written.Discarded,
                visit.Written.Clamped);
        }

        NoticeWhatTheStreamDeclared(stream, visit);

        await TellTheTunerWhatHappenedAsync(stream, visit.Outcome, abort);
        await RecordAsync(stream, visit, took, abort);
    }

    private async Task<bool> WaitedForATunerAsync(int attempt, CancellationToken walking, CancellationToken abort)
    {
        try
        {
            await Task.Delay(settings.WhenTunersAreFull.DelayAfter(attempt), clock, walking);

            return true;
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            logger.LogInformation("The driver went away while the walk waited for a tuner.");

            return false;
        }
    }

    private async Task TellTheTunerWhatHappenedAsync(
        BroadcastStream stream,
        VisitOutcome outcome,
        CancellationToken abort)
    {
        if (stream.TunedWith is not { } candidateChannelId)
        {
            return;
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;

        if (CollectionBackOff.IsWorthReportingToTheTuner(outcome))
        {
            await tuning.ReportFailureAsync(candidateChannelId, at, abort);

            return;
        }

        if (outcome is VisitOutcome.Interrupted)
        {
            return;
        }

        await tuning.ReportReachedAsync(candidateChannelId, at, abort);
    }

    private void NoticeWhatTheStreamDeclared(BroadcastStream stream, VisitResult visit)
    {
        ServiceId[] declared =
        [
            .. visit.Descriptions
                .Where(description => description.IsActualStream
                    && description.TransportStreamId == stream.TransportStreamId.Value)
                .SelectMany(description => description.Services)
                .Select(service => new ServiceId(service.ServiceId)),
        ];

        if (declared.Length == 0)
        {
            return;
        }

        rescans.Post(RescanHints.Between(
            stream.NetworkId,
            stream.TransportStreamId,
            declared,
            stream.Services));
    }

    private async Task<VisitResult?> VisitAsync(
        BroadcastStream stream,
        bool hurried,
        long began,
        CancellationToken walking,
        CancellationToken abort)
    {
        try
        {
            return await visitor.VisitAsync(stream.Tuning, hurried, walking);
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            logger.LogInformation(
                "The driver went away mid-walk; {NetworkId}-{TransportStreamId} is recorded as interrupted.",
                stream.NetworkId.Value,
                stream.TransportStreamId.Value);
            await RecordAsync(
                stream,
                new VisitResult(VisitOutcome.Interrupted, new ProgrammesWritten(0, 0, 0), null),
                clock.GetElapsedTime(began),
                abort);

            return null;
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
    }

    private async Task RecordAsync(
        BroadcastStream stream,
        VisitResult visit,
        TimeSpan took,
        CancellationToken abort)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<VisitTally> counted = TallyOf(stream, visit);
        bool heard = counted.Count > 0;
        bool reached = heard && await ReachedTheGoalAsync(stream, at, abort);
        StreamVisit? held = await visits.FindAsync(stream.NetworkId, stream.TransportStreamId, abort);

        if (held is null)
        {
            StreamVisit first = StreamVisit.Record(
                stream.NetworkId,
                stream.TransportStreamId,
                visit.Outcome,
                at,
                took,
                heardTheSchedule: heard,
                reachedTheGoal: reached);

            first.Tallied(counted);

            await visits.SaveAsync(first, abort);
            events.Signal(AppEventName.EpgCollection);

            return;
        }

        held.Record(visit.Outcome, at, took, heardTheSchedule: heard, reachedTheGoal: reached);

        if (!visit.WorthWaitingOut)
        {
            held.Tallied(counted);
        }

        await visits.SaveAsync(held, abort);
        events.Signal(AppEventName.EpgCollection);
    }

    private async Task<bool> ReachedTheGoalAsync(BroadcastStream stream, DateTime at, CancellationToken abort)
    {
        var coveredUntil = new List<DateTime?>(stream.Services.Count);

        foreach (ServiceId service in stream.Services)
        {
            coveredUntil.Add(await programmes.CoveredUntilAsync(
                stream.NetworkId.Value,
                service.Value,
                abort));
        }

        return GuideCoverage.IsMetByEveryServiceHoldingAGuide(coveredUntil, at, settings.WantedCoverage);
    }

    private static IReadOnlyList<VisitTally> TallyOf(BroadcastStream stream, VisitResult visit)
        =>
        [
            .. visit.Tally
                .Where(counted => counted.Service.NetworkId == stream.NetworkId.Value
                    && counted.Service.TransportStreamId == stream.TransportStreamId.Value)
                .Select(counted => VisitTally.Rehydrate(
                    stream.NetworkId,
                    stream.TransportStreamId,
                    new ServiceId(counted.Service.ServiceId),
                    counted.TableId,
                    counted.LastTableId,
                    counted.SegmentsDeclared,
                    counted.SegmentsHeard,
                    counted.SectionsDeclared,
                    counted.SectionsHeard,
                    counted.VersionChanges)),
        ];

    private sealed record TurnedAway(BroadcastStream Stream, VisitResult Visit, TimeSpan Took);

    private sealed record Pass(int Visited, int Gathered, int CameBackShort, bool TheDriverWentAway);

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
