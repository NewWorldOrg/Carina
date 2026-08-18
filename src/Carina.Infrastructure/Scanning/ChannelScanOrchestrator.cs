using Carina.Broadcast.Tables;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Scans;

namespace Carina.Infrastructure.Scanning;

public sealed class ChannelScanOrchestrator : IChannelScanOrchestrator
{
    public const string BusyReason = "every tuner was busy for longer than the bounded wait";

    public const string CancelledReason = ScanConclusion.CancelledReason;

    private readonly IDriverSignals signals;
    private readonly IScanRunRepository runs;
    private readonly IBroadcastServiceRepository services;
    private readonly ICandidateChannelRepository candidates;
    private readonly ISatelliteTransportStreamRepository satelliteStreams;
    private readonly IAppEventPublisher events;
    private readonly TimeProvider clock;
    private readonly ScanSettings settings;
    private readonly TunedStreamProbe prober;
    private readonly IDriverClient driver;

    public ChannelScanOrchestrator(
        IDriverClient driver,
        IDriverSignals signals,
        IScanRunRepository runs,
        IBroadcastServiceRepository services,
        ICandidateChannelRepository candidates,
        ISatelliteTransportStreamRepository satelliteStreams,
        IAppEventPublisher events,
        TimeProvider clock,
        ScanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(settings);

        this.driver = driver;
        this.signals = signals;
        this.runs = runs;
        this.services = services;
        this.candidates = candidates;
        this.satelliteStreams = satelliteStreams;
        this.events = events;
        this.clock = clock;
        this.settings = settings;

        prober = new TunedStreamProbe(driver, settings, clock);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<ScanOutcome> RunAsync(ScanScope scope, CancellationToken cancellationToken)
        => RunAsync(scope, UnwatchedScanRun.Instance, cancellationToken);

    public async Task<ScanOutcome> RunAsync(
        ScanScope scope,
        IScanRunObserver observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(observer);

        DriverCall<DriverHello> greeting = await driver.GetHealthAsync(cancellationToken);

        if (!greeting.TryGetValue(out DriverHello? hello))
        {
            return ScanOutcome.CouldNotStart(
                greeting.Failure ?? "The driver did not answer, so no tuner can be asked to scan.");
        }

        ScanRunStart start = await runs.StartAsync(
            ScanRun.Start(ScanRunId.New(), hello.InstanceId, Now),
            cancellationToken);

        if (start.Started is not { } run)
        {
            return ScanOutcome.RefusedBecauseOneIsRunning(start.AlreadyRunning);
        }

        observer.Started(run);

        using var interruption = new CancellationTokenSource();
        using IDisposable subscription = signals.Subscribe(name =>
        {
            if (string.Equals(name, DriverClientSignals.InstanceChanged, StringComparison.Ordinal))
            {
                Stop(interruption);
            }
        });

        return await WalkAsync(run, scope, observer, interruption, cancellationToken);
    }

    private async Task<ScanOutcome> WalkAsync(
        ScanRun run,
        ScanScope scope,
        IScanRunObserver observer,
        CancellationTokenSource interruption,
        CancellationToken cancellationToken)
    {
        using var walking = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            interruption.Token);

        IReadOnlyList<TuningParameters> targets = await ScanTargets.WalkAsync(scope, satelliteStreams, cancellationToken);
        var attempts = new List<ScanRunAttempt>();
        var carried = new Dictionary<TuningParameters, StreamProbe>();
        var probed = new Dictionary<TuningParameters, StreamProbe>();
        var streamsSeen = new Dictionary<(int Network, int Stream), TuningParameters>();
        string? failure = null;

        foreach (TuningParameters target in targets)
        {
            if (walking.IsCancellationRequested)
            {
                break;
            }

            DateTime startedAt = Now;
            StreamProbe probe;

            try
            {
                probe = await AttemptAsync(target, walking.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (probe.Verdict is ProbeVerdict.DriverUnreachable)
            {
                failure = probe.Detail;

                break;
            }

            if (probe.Verdict is ProbeVerdict.TunersBusy)
            {
                failure = Busy(probe.Detail);

                break;
            }

            string? detail = probe.Detail;

            if (probe.Outcome is ScanAttemptOutcome.Succeeded && probe.Description is { } description)
            {
                probed[target] = probe;

                (int OriginalNetworkId, int TransportStreamId) stream = (description.OriginalNetworkId, description.TransportStreamId);

                if (target.System is TuneSystem.IsdbSBs
                    && streamsSeen.TryGetValue(stream, out TuningParameters? first))
                {
                    detail = $"This slot is not there: it carries the same stream as"
                        + $" {ScanTargetNames.Of(first)}, so it is proposed once.";
                }
                else
                {
                    streamsSeen[stream] = target;
                    carried[target] = probe;
                }
            }

            attempts.Add(ScanRunAttempt.Rehydrate(
                ScanRunAttemptId.New(),
                run.Id,
                target,
                probe.Outcome,
                probe.Measurement,
                probe.ObservedTransportStreamId,
                detail,
                startedAt,
                Now));

            await runs.AddAttemptAsync(attempts[^1], CancellationToken.None);
        }

        return await ConcludeAsync(
            run, attempts, carried, probed, observer, interruption, failure, cancellationToken);
    }

    private async Task<ScanOutcome> ConcludeAsync(
        ScanRun run,
        IReadOnlyList<ScanRunAttempt> attempts,
        IReadOnlyDictionary<TuningParameters, StreamProbe> carried,
        IReadOnlyDictionary<TuningParameters, StreamProbe> probed,
        IScanRunObserver observer,
        CancellationTokenSource interruption,
        string? failure,
        CancellationToken cancellationToken)
    {
        ScanDifference difference = ScanDifference.Nothing;

        if (interruption.IsCancellationRequested)
        {
            run.Interrupt(Now);
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            ScanConclusion.Stop(run, observer.Stop, Now);
        }
        else if (failure is { } reason)
        {
            run.Fail(reason, Now);
        }
        else
        {
            IReadOnlyList<RotationDeparture> departures = await TurnTheRotationAsync(attempts, probed);
            difference = await DifferenceAsync(carried, departures);
            run.Complete(Now);
        }

        await runs.SaveAsync(run, CancellationToken.None);
        events.Signal(AppEventName.Tuners);

        return ScanOutcome.Of(run, attempts, difference);
    }

    private async Task<StreamProbe> AttemptAsync(TuningParameters target, CancellationToken abort)
    {
        int refusals = 0;

        while (true)
        {
            using var deadline = new CancellationTokenSource();
            using ITimer? timer = settings.AttemptsAreBounded
                ? clock.CreateTimer(
                    _ => Stop(deadline),
                    null,
                    settings.AttemptPatience,
                    Timeout.InfiniteTimeSpan)
                : null;

            StreamProbe probe = await prober.ProbeAsync(target, deadline.Token, abort);

            if (probe.Verdict is not ProbeVerdict.TunersBusy)
            {
                return probe;
            }

            refusals++;

            if (refusals >= settings.BusyWait.FailureCeiling)
            {
                return probe;
            }

            await Task.Delay(settings.BusyWait.DelayAfter(refusals), clock, abort);
        }
    }

    private async Task<IReadOnlyList<RotationDeparture>> TurnTheRotationAsync(
        IReadOnlyList<ScanRunAttempt> attempts,
        IReadOnlyDictionary<TuningParameters, StreamProbe> probed)
    {
        var walked = attempts.ToDictionary(attempt => attempt.Tuning, attempt => attempt);
        var departures = new List<RotationDeparture>();
        DateTime at = Now;

        foreach (BroadcastService service in await services.ListAsync(CancellationToken.None))
        {
            IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                CancellationToken.None);

            foreach (CandidateChannel candidate in stored)
            {
                if (!walked.TryGetValue(candidate.Tuning, out ScanRunAttempt? attempt))
                {
                    continue;
                }

                bool wasInRotation = candidate.IsInRotation;

                if (attempt.Outcome is ScanAttemptOutcome.Succeeded
                    && probed.TryGetValue(candidate.Tuning, out StreamProbe? probe)
                    && Names(probe, service.ServiceId))
                {
                    candidate.RecordTuningSuccess(
                        probe.Measurement ?? SignalMeasurement.WithLock(at),
                        at);
                }
                else
                {
                    candidate.RecordTuningFailure(settings.Rotation, at);

                    if (wasInRotation && !candidate.IsInRotation)
                    {
                        departures.Add(new RotationDeparture(
                            candidate.NetworkId,
                            candidate.ServiceId,
                            candidate.Tuning,
                            candidate.ConsecutiveFailures,
                            candidate.NeedsAttentionSince ?? at));
                    }
                }

                await candidates.SaveAsync(candidate, CancellationToken.None);
            }
        }

        return departures;
    }

    private async Task<ScanDifference> DifferenceAsync(
        IReadOnlyDictionary<TuningParameters, StreamProbe> carried,
        IReadOnlyList<RotationDeparture> departures)
    {
        Dictionary<(NetworkId, ServiceId), ObservedService> observed = Observe(carried);
        var reached = carried.Keys.ToHashSet();
        var changes = new List<ScanServiceChange>();

        foreach (BroadcastService service in await services.ListAsync(CancellationToken.None))
        {
            IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                CancellationToken.None);
            CandidateChannel[] inScope = stored.Where(candidate => reached.Contains(candidate.Tuning)).ToArray();
            (NetworkId NetworkId, ServiceId ServiceId) key = (service.NetworkId, service.ServiceId);

            if (!observed.Remove(key, out ObservedService? seen))
            {
                if (inScope.Length == 0)
                {
                    continue;
                }

                IReadOnlyList<ScanChannelChange> gone = MissingChannels(inScope);

                changes.Add(new ScanServiceChange(
                    inScope.Length == stored.Count ? ScanChangeKind.Missing : ScanChangeKind.Updated,
                    service.NetworkId,
                    service.ServiceId,
                    service.Name,
                    service.Category,
                    gone,
                    Seen: false));

                continue;
            }

            ScanChannelChange[] added = seen.Channels
                .Where(channel => stored.All(candidate => !candidate.Tuning.Equals(channel.Tuning)))
                .ToArray();
            IReadOnlyList<ScanChannelChange> missing = MissingChannels(inScope
                .Where(candidate => seen.Channels.All(channel => !channel.Tuning.Equals(candidate.Tuning))));
            bool described = service.Name != seen.Name || service.Category != seen.Category;
            bool renumbered = seen.RemoteControlKeyId is not null
                && service.RemoteControlKeyId != seen.RemoteControlKeyId;

            if (added.Length > 0 || missing.Count > 0 || described || renumbered)
            {
                changes.Add(new ScanServiceChange(
                    ScanChangeKind.Updated,
                    service.NetworkId,
                    service.ServiceId,
                    seen.Name,
                    seen.Category,
                    [.. added, .. missing],
                    Seen: true)
                {
                    RemoteControlKeyId = seen.RemoteControlKeyId,
                });
            }
        }

        changes.AddRange(observed.Values.Select(seen => new ScanServiceChange(
            ScanChangeKind.Added,
            seen.NetworkId,
            seen.ServiceId,
            seen.Name,
            seen.Category,
            seen.Channels,
            Seen: true)
        {
            RemoteControlKeyId = seen.RemoteControlKeyId,
        }));

        return new ScanDifference(changes, departures);
    }

    private static Dictionary<(NetworkId, ServiceId), ObservedService> Observe(
        IReadOnlyDictionary<TuningParameters, StreamProbe> carried)
    {
        var observed = new Dictionary<(NetworkId, ServiceId), ObservedService>();

        foreach ((TuningParameters? target, StreamProbe? probe) in carried)
        {
            HarvestedDescription description = probe.Description!;
            IReadOnlyList<int> partiallyReceived = probe.Network!
                .PartiallyReceivedServicesOf(description.TransportStreamId);
            var networkId = new NetworkId(description.OriginalNetworkId);
            var streamId = new TransportStreamId(description.TransportStreamId);
            int? remoteControlKeyId = probe.Network!
                .TransportStreams
                .FirstOrDefault(stream => stream.TransportStreamId == description.TransportStreamId)?
                .RemoteControlKeyId;

            foreach (DescribedService described in description.Services)
            {
                var serviceId = new ServiceId(described.ServiceId);
                var channel = new ScanChannelChange(
                    ScanChangeKind.Added,
                    target,
                    streamId,
                    probe.Measurement);

                if (observed.TryGetValue((networkId, serviceId), out ObservedService? already))
                {
                    already.Channels.Add(channel);

                    continue;
                }

                observed[(networkId, serviceId)] = new ObservedService(
                    networkId,
                    serviceId,
                    described.Name,
                    ServiceCategories.Of(
                        described.Kind,
                        partiallyReceived.Contains(described.ServiceId)),
                    [channel])
                {
                    RemoteControlKeyId = remoteControlKeyId,
                };
            }
        }

        return observed;
    }

    private static IReadOnlyList<ScanChannelChange> MissingChannels(IEnumerable<CandidateChannel> stored)
        => [.. stored.Select(candidate => new ScanChannelChange(
            ScanChangeKind.Missing,
            candidate.Tuning,
            candidate.Tuning.TransportStreamId,
            candidate.LastMeasurement))];

    private static bool Names(StreamProbe probe, ServiceId serviceId)
        => probe.Description!.Services.Any(service => service.ServiceId == serviceId.Value);

    private static string Busy(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return BusyReason;
        }

        string said = $"{BusyReason}. The driver said: {detail}";

        return said.Length <= ScanRun.ReasonMaxLength ? said : said[..ScanRun.ReasonMaxLength];
    }

    private static void Stop(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed record ObservedService(
        NetworkId NetworkId,
        ServiceId ServiceId,
        string Name,
        ServiceCategory Category,
        List<ScanChannelChange> Channels)
    {
        public int? RemoteControlKeyId { get; init; }
    }
}
