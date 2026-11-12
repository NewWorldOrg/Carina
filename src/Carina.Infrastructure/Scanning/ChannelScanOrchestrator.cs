using Carina.Broadcast.Tables;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Scans;

using Puppis.Contracts;

namespace Carina.Infrastructure.Scanning;

public sealed class ChannelScanOrchestrator : IChannelScanOrchestrator
{
    public const string BusyReason = "every tuner was busy for longer than the bounded wait";

    public const string CancelledReason = ScanConclusion.CancelledReason;

    private readonly IDriverSignals signals;
    private readonly IScanRunRepository runs;
    private readonly ChannelRotationDomainService rotation;
    private readonly ChannelLineupDomainService lineup;
    private readonly IAppEventPublisher events;
    private readonly TimeProvider clock;
    private readonly ScanSettings settings;
    private readonly TunedStreamProbe prober;
    private readonly IDriverClient driver;
    private readonly ScanPlanningDomainService planning;

    public ChannelScanOrchestrator(
        IDriverClient driver,
        ScanPlanningDomainService planning,
        IDriverSignals signals,
        IScanRunRepository runs,
        ChannelRotationDomainService rotation,
        ChannelLineupDomainService lineup,
        IAppEventPublisher events,
        TimeProvider clock,
        ScanSettings settings)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(settings);

        this.driver = driver;
        this.planning = planning;
        this.signals = signals;
        this.runs = runs;
        this.rotation = rotation;
        this.lineup = lineup;
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

        if (await planning.PlanAsync(scope, cancellationToken) is not { } plan)
        {
            return ScanOutcome.NothingATunerCanReceive(
                $"No tuner in service receives {string.Join(", ", scope.Systems.Select(TuneSystemConverter.WireName))},"
                + " so there is nothing this scan could walk.");
        }

        ScanRunStart start = await runs.StartAsync(
            ScanRun.Start(ScanRunId.New(), hello.InstanceId, Now),
            cancellationToken);

        if (start.Started is not { } run)
        {
            return ScanOutcome.RefusedBecauseOneIsRunning(start.AlreadyRunning);
        }

        observer.Started(run);

        using CancellationTokenSource interruption = new();
        using IDisposable subscription = signals.Subscribe(name =>
        {
            if (string.Equals(name, DriverClientSignals.InstanceChanged, StringComparison.Ordinal))
            {
                Stop(interruption);
            }
        });

        return await WalkAsync(run, plan.Targets, observer, interruption, cancellationToken);
    }

    private async Task<ScanOutcome> WalkAsync(
        ScanRun run,
        IReadOnlyList<TuningParameters> targets,
        IScanRunObserver observer,
        CancellationTokenSource interruption,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource walking = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            interruption.Token);

        var attempts = new List<ScanRunAttempt>();
        var carried = new Dictionary<TuningParameters, StreamProbe>();
        var probed = new Dictionary<TuningParameters, StreamProbe>();
        var twins = new SatelliteTwinSlots();
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

                if (twins.TryFindTwin(target, description.OriginalNetworkId, description.TransportStreamId, out TuningParameters? first))
                {
                    detail = $"This slot is not there: it carries the same stream as"
                        + $" {ScanTargetNames.Of(first)}, so it is proposed once.";
                }
                else
                {
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
        ScanWalkEnd end = new(
            interruption.IsCancellationRequested,
            cancellationToken.IsCancellationRequested ? observer.Stop : null,
            failure);
        ScanDifference difference = ScanDifference.Nothing;

        if (end.WalkedThrough)
        {
            IReadOnlyList<RotationDeparture> departures = await rotation.RecordScanAsync(
                attempts,
                Named(probed),
                settings.Rotation,
                Now,
                CancellationToken.None);
            difference = await lineup.ProposeAsync(Observe(carried), [.. carried.Keys], departures, CancellationToken.None);
        }

        ScanConclusion.Settle(run, end, Now);

        await runs.SaveAsync(run, CancellationToken.None);
        events.Signal(AppEventName.Tuners);

        return ScanOutcome.Of(run, attempts, difference);
    }

    private async Task<StreamProbe> AttemptAsync(TuningParameters target, CancellationToken abort)
    {
        int refusals = 0;

        while (true)
        {
            using CancellationTokenSource deadline = new();
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

    private static IReadOnlyDictionary<TuningParameters, IReadOnlySet<ServiceId>> Named(
        IReadOnlyDictionary<TuningParameters, StreamProbe> probed)
        => probed.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlySet<ServiceId>)entry.Value.Description!.Services
                .Select(service => new ServiceId(service.ServiceId))
                .ToHashSet());

    private static IReadOnlyList<ScannedService> Observe(IReadOnlyDictionary<TuningParameters, StreamProbe> carried)
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

        return
        [
            .. observed.Values.Select(seen => new ScannedService(
                seen.NetworkId,
                seen.ServiceId,
                seen.Name,
                seen.Category,
                seen.RemoteControlKeyId,
                seen.Channels)),
        ];
    }

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
