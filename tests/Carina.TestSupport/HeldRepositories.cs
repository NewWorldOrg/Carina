using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Scans;

namespace Carina.TestSupport;

public sealed class HeldScanRuns : IScanRunRepository
{
    public List<ScanRun> Runs { get; } = [];

    public List<ScanRunAttempt> Attempts { get; } = [];

    public Task<ScanRunStart> StartAsync(ScanRun run, CancellationToken cancellationToken)
    {
        if (Runs.FirstOrDefault(held => held.IsRunning) is { } running)
        {
            return Task.FromResult(ScanRunStart.RefusedBecauseOneIsRunning(running.Id));
        }

        Runs.Add(run);

        return Task.FromResult(ScanRunStart.Of(run));
    }

    public Task<ScanRun?> FindAsync(ScanRunId id, CancellationToken cancellationToken)
        => Task.FromResult(Runs.FirstOrDefault(run => run.Id.Equals(id)));

    public Task<ScanRun?> FindRunningAsync(CancellationToken cancellationToken)
        => Task.FromResult(Runs.FirstOrDefault(run => run.IsRunning));

    public Task<IReadOnlyList<ScanRun>> ListRecentAsync(int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ScanRun>>(
            [.. Runs.OrderByDescending(run => run.StartedAt).Take(limit)]);

    public Task SaveAsync(ScanRun run, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddAttemptAsync(ScanRunAttempt attempt, CancellationToken cancellationToken)
    {
        Attempts.Add(attempt);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScanRunAttempt>> ListAttemptsAsync(
        ScanRunId id,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ScanRunAttempt>>(
            [.. Attempts.Where(attempt => attempt.ScanRunId.Equals(id))]);
}

public sealed class HeldServices : IBroadcastServiceRepository
{
    public List<BroadcastService> Services { get; } = [];

    public Task<BroadcastService?> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Services.FirstOrDefault(service =>
            service.NetworkId.Equals(networkId) && service.ServiceId.Equals(serviceId)));

    public Task<IReadOnlyList<BroadcastService>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BroadcastService>>([.. Services]);

    public Task AddAsync(BroadcastService service, CancellationToken cancellationToken)
    {
        Services.Add(service);

        return Task.CompletedTask;
    }

    public Task SaveAsync(BroadcastService service, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RemoveAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken)
    {
        Services.RemoveAll(service =>
            service.NetworkId.Equals(networkId) && service.ServiceId.Equals(serviceId));

        return Task.CompletedTask;
    }
}

public sealed class UnguardedWrites : IAtomicWrite
{
    public Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        return write(cancellationToken);
    }
}

public sealed class HeldCandidates : ICandidateChannelRepository
{
    public List<CandidateChannel> Candidates { get; } = [];

    public int Saves { get; private set; }

    public Task<CandidateChannel?> FindAsync(CandidateChannelId id, CancellationToken cancellationToken)
        => Task.FromResult(Candidates.FirstOrDefault(candidate => candidate.Id.Equals(id)));

    public Task<IReadOnlyList<CandidateChannel>> ListForServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CandidateChannel>>([.. Of(networkId, serviceId)]);

    public Task<CandidateChannel?> FindSelectedAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Of(networkId, serviceId).FirstOrDefault(candidate => candidate.IsSelected));

    public Task<IReadOnlyList<CandidateChannel>> ListInRotationAsync(
        DateTime at,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CandidateChannel>>(
            [.. Candidates.Where(candidate =>
                candidate.IsInRotation
                && (candidate.NextAttemptAt is null || candidate.NextAttemptAt <= at))]);

    public Task<IReadOnlyList<CandidateChannel>> ListNeedingAttentionAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CandidateChannel>>(
            [.. Candidates.Where(candidate => !candidate.IsInRotation)]);

    public Task AddAsync(CandidateChannel candidate, CancellationToken cancellationToken)
    {
        Candidates.Add(candidate);

        return Task.CompletedTask;
    }

    public Task SaveAsync(CandidateChannel candidate, CancellationToken cancellationToken)
    {
        Saves++;

        return Task.CompletedTask;
    }

    public Task<CandidateChannel?> SelectAsync(
        CandidateChannelId id,
        SelectionSource source,
        SignalMeasurement? measuredAtSelection,
        DateTime at,
        CancellationToken cancellationToken)
    {
        var chosen = Candidates.FirstOrDefault(candidate => candidate.Id.Equals(id));

        if (chosen is null)
        {
            return Task.FromResult<CandidateChannel?>(null);
        }

        foreach (var candidate in Of(chosen.NetworkId, chosen.ServiceId).Where(held => held.IsSelected))
        {
            candidate.Deselect();
        }

        chosen.Select(source, measuredAtSelection, at);

        return Task.FromResult<CandidateChannel?>(chosen);
    }

    public Task ClearSelectionAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in Of(networkId, serviceId).Where(held => held.IsSelected))
        {
            candidate.Deselect();
        }

        return Task.CompletedTask;
    }

    public Task RequireRevalidationAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in Candidates)
        {
            candidate.RequireRevalidation();
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(CandidateChannelId id, CancellationToken cancellationToken)
    {
        Candidates.RemoveAll(candidate => candidate.Id.Equals(id));

        return Task.CompletedTask;
    }

    private IEnumerable<CandidateChannel> Of(NetworkId networkId, ServiceId serviceId)
        => Candidates.Where(candidate =>
            candidate.NetworkId.Equals(networkId) && candidate.ServiceId.Equals(serviceId));
}

public sealed class HeldSatelliteStreams : ISatelliteTransportStreamRepository
{
    public List<SatelliteTransportStream> Streams { get; } = [];

    public Task<IReadOnlyList<SatelliteTransportStream>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SatelliteTransportStream>>([.. Streams]);

    public Task<IReadOnlyList<SatelliteTransportStream>> ListForSlotAsync(
        int bsChannel,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SatelliteTransportStream>>(
            [.. Streams.Where(stream => stream.BsChannel == bsChannel)]);

    public Task ReplaceSlotAsync(
        int bsChannel,
        IReadOnlyList<SatelliteTransportStream> streams,
        CancellationToken cancellationToken)
    {
        Streams.RemoveAll(stream => stream.BsChannel == bsChannel);
        Streams.AddRange(streams);

        return Task.CompletedTask;
    }
}

public sealed class RecordingAppEvents : IAppEventPublisher
{
    private readonly Lock gate = new();
    private readonly List<string> signalled = [];

    public IReadOnlyList<string> Signalled
    {
        get
        {
            lock (gate)
            {
                return [.. signalled];
            }
        }
    }

    public void Signal(AppEventName name)
    {
        lock (gate)
        {
            signalled.Add(name.Value);
        }
    }
}
