using Carina.Contracts;
using Carina.Driver.Tuning;

namespace Carina.Driver.Sessions;

public enum PoolVerdict
{
    Granted,
    Shared,
    NoDeviceFree,
    DeviceBusy,
}

public sealed record PoolRequest(
    SessionId SessionId,
    SessionPurpose Purpose,
    TuningKey Tuning,
    string? DeviceId,
    IReadOnlyList<string> Candidates
)
{
    public int Priority => SessionPriority.Of(Purpose);

    public IReadOnlyList<string> Allowed => DeviceId is { } named ? [named] : Candidates;
}

public sealed record PoolGrant(
    PoolVerdict Verdict,
    string DeviceId,
    SessionId Holder,
    bool NeedsTuning,
    IReadOnlyList<SessionId> Displaced,
    string Detail
)
{
    public static PoolGrant Refused(PoolVerdict verdict, string detail) =>
        new(verdict, string.Empty, default, false, [], detail);

    public bool IsGranted => Verdict is PoolVerdict.Granted or PoolVerdict.Shared;
}

public sealed class TunerPool(TimeProvider timeProvider, TimeSpan? grace = null) : IDisposable
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);

    private sealed record Sink(SessionId SessionId, int Priority);

    private sealed class Lease(string deviceId, TuningKey tuning, SessionId holder)
    {
        public string DeviceId { get; } = deviceId;

        public TuningKey Tuning { get; set; } = tuning;

        public SessionId Holder { get; set; } = holder;

        public List<Sink> Sinks { get; } = [];

        public ITunerDevice? Device { get; set; }

        public Exception? TuneFailure { get; set; }

        public DateTimeOffset? IdleSince { get; set; }

        public ManualResetEventSlim Ready { get; set; } = new(false);

        public bool Established { get; set; }

        public int Priority =>
            Sinks.Count is 0 ? SessionPriority.Unknown : Sinks.Max(sink => sink.Priority);

        public bool IsIdle => Sinks.Count is 0;

        public bool IsUsable => TuneFailure is null;
    }

    private readonly Dictionary<string, Lease> leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITunerDevice> retiring = new(StringComparer.Ordinal);
    private readonly Dictionary<SessionId, Lease> bySink = [];
    private readonly Lock gate = new();
    private readonly TimeSpan linger = grace ?? DefaultGrace;

    public TimeSpan Grace => linger;

    public PoolGrant Acquire(PoolRequest request)
    {
        PoolGrant grant;
        List<ITunerDevice> closing;

        lock (gate)
        {
            closing = Expire();

            grant =
                RideAlong(request)
                ?? TakeAFreeTuner(request)
                ?? TakeATunerNobodyIsOn(request)
                ?? TakeATunerFromSomethingLessImportant(request)
                ?? TurnAway(request);
        }

        foreach (var device in closing)
        {
            Close(device);
        }

        return grant;
    }

    private PoolGrant? RideAlong(PoolRequest request)
    {
        foreach (var lease in LeasesFor(request))
        {
            if (!lease.IsUsable || lease.Tuning != request.Tuning)
            {
                continue;
            }

            if (lease.IsIdle)
            {
                return Rehold(lease, request);
            }

            Attach(lease, request);

            return new PoolGrant(
                PoolVerdict.Shared,
                lease.DeviceId,
                lease.Holder,
                NeedsTuning: false,
                [],
                $"The tuner '{lease.DeviceId}' is already on {request.Tuning}, so '{request.SessionId}' rides the stream '{lease.Holder}' is reading."
            );
        }

        return null;
    }

    private PoolGrant Rehold(Lease lease, PoolRequest request)
    {
        var wasOpen = lease.Device is not null;

        lease.Holder = request.SessionId;
        lease.Established = false;
        lease.Ready = new ManualResetEventSlim(false);
        Attach(lease, request);

        return new PoolGrant(
            PoolVerdict.Granted,
            lease.DeviceId,
            request.SessionId,
            NeedsTuning: !wasOpen,
            [],
            wasOpen
                ? $"The tuner '{lease.DeviceId}' was still on {request.Tuning}, so '{request.SessionId}' takes it back without tuning it again."
                : $"The tuner '{lease.DeviceId}' is given to '{request.SessionId}' for {request.Tuning}."
        );
    }

    private PoolGrant? TakeAFreeTuner(PoolRequest request)
    {
        foreach (var deviceId in request.Allowed)
        {
            if (leases.ContainsKey(deviceId))
            {
                continue;
            }

            var lease = new Lease(deviceId, request.Tuning, request.SessionId);
            leases[deviceId] = lease;
            Attach(lease, request);

            return new PoolGrant(
                PoolVerdict.Granted,
                deviceId,
                request.SessionId,
                NeedsTuning: true,
                [],
                $"The tuner '{deviceId}' is free and goes to '{request.SessionId}' for {request.Tuning}."
            );
        }

        return null;
    }

    private PoolGrant? TakeATunerNobodyIsOn(PoolRequest request)
    {
        foreach (var lease in LeasesFor(request))
        {
            if (!lease.IsIdle || !lease.IsUsable)
            {
                continue;
            }

            return Retune(
                lease,
                request,
                [],
                $"The tuner '{lease.DeviceId}' was being held for whoever came back to {lease.Tuning}; nobody did, so '{request.SessionId}' takes it for {request.Tuning}."
            );
        }

        return null;
    }

    private PoolGrant? TakeATunerFromSomethingLessImportant(PoolRequest request)
    {
        var loser = LeasesFor(request)
            .Where(lease => lease.IsUsable && !lease.IsIdle && lease.Priority < request.Priority)
            .OrderBy(lease => lease.Priority)
            .FirstOrDefault();

        if (loser is null)
        {
            return null;
        }

        var displaced = loser.Sinks.Select(sink => sink.SessionId).ToArray();
        var names = string.Join("', '", displaced.Select(session => session.Value));

        return Retune(
            loser,
            request,
            displaced,
            $"The tuner '{loser.DeviceId}' goes to '{request.SessionId}' for {request.Purpose.ToString().ToLowerInvariant()} on {request.Tuning}, which outranks '{names}'."
        );
    }

    private PoolGrant Retune(
        Lease lease,
        PoolRequest request,
        IReadOnlyList<SessionId> displaced,
        string detail
    )
    {
        foreach (var sink in lease.Sinks)
        {
            bySink.Remove(sink.SessionId);
        }

        lease.Sinks.Clear();

        if (lease.Device is { } open)
        {
            Retire(lease.DeviceId, open);
        }

        lease.Device = null;
        lease.TuneFailure = null;
        lease.Tuning = request.Tuning;
        lease.Holder = request.SessionId;
        lease.Established = false;
        lease.Ready = new ManualResetEventSlim(false);
        Attach(lease, request);

        return new PoolGrant(
            PoolVerdict.Granted,
            lease.DeviceId,
            request.SessionId,
            NeedsTuning: true,
            displaced,
            detail
        );
    }

    private PoolGrant TurnAway(PoolRequest request)
    {
        if (request.DeviceId is { } named)
        {
            return PoolGrant.Refused(
                PoolVerdict.DeviceBusy,
                leases.TryGetValue(named, out var lease)
                    ? WhyNot(lease, request)
                    : $"The tuner '{named}' cannot be had for {request.Tuning}."
            );
        }

        var reasons = request
            .Candidates.Select(deviceId =>
                leases.TryGetValue(deviceId, out var lease)
                    ? WhyNot(lease, request)
                    : $"The tuner '{deviceId}' cannot be had."
            )
            .ToArray();

        return PoolGrant.Refused(
            PoolVerdict.NoDeviceFree,
            reasons.Length is 0
                ? $"No tuner can serve {request.Tuning}."
                : string.Join(" ", reasons)
        );
    }

    private static string WhyNot(Lease lease, PoolRequest request) =>
        lease.TuneFailure is { } failure
            ? $"The tuner '{lease.DeviceId}' is held back after it could not be tuned: {failure.Message}"
            : $"The tuner '{lease.DeviceId}' is on {lease.Tuning} for '{string.Join("', '", lease.Sinks.Select(sink => sink.SessionId.Value))}', which '{request.SessionId}' does not outrank.";

    private void Attach(Lease lease, PoolRequest request)
    {
        lease.IdleSince = null;
        lease.Sinks.Add(new Sink(request.SessionId, request.Priority));
        bySink[request.SessionId] = lease;
    }

    public void Tuned(string deviceId, ITunerDevice device)
    {
        lock (gate)
        {
            if (leases.TryGetValue(deviceId, out var lease))
            {
                lease.Device = device;

                return;
            }
        }

        device.Dispose();
    }

    public void Ready(string deviceId)
    {
        lock (gate)
        {
            if (!leases.TryGetValue(deviceId, out var lease))
            {
                return;
            }

            lease.Established = true;
            lease.Ready.Set();
        }
    }

    public void TuningFailed(string deviceId, Exception cause)
    {
        lock (gate)
        {
            if (!leases.TryGetValue(deviceId, out var lease))
            {
                return;
            }

            foreach (var sink in lease.Sinks)
            {
                bySink.Remove(sink.SessionId);
            }

            lease.Sinks.Clear();
            lease.TuneFailure = cause;
            lease.Established = false;
            lease.IdleSince = timeProvider.GetUtcNow();
            lease.Ready.Set();
        }
    }

    public bool AwaitReady(string deviceId, TimeSpan limit)
    {
        ManualResetEventSlim ready;

        lock (gate)
        {
            if (!leases.TryGetValue(deviceId, out var lease))
            {
                return false;
            }

            if (lease.Established)
            {
                return true;
            }

            ready = lease.Ready;
        }

        if (!ready.Wait(limit))
        {
            return false;
        }

        lock (gate)
        {
            return leases.TryGetValue(deviceId, out var lease) && lease.Established;
        }
    }

    public ITunerDevice? DeviceOf(string deviceId)
    {
        lock (gate)
        {
            return leases.TryGetValue(deviceId, out var lease) ? lease.Device : null;
        }
    }

    public void HandOver(string deviceId)
    {
        ITunerDevice? outgoing;

        lock (gate)
        {
            if (!retiring.Remove(deviceId, out outgoing))
            {
                return;
            }
        }

        Close(outgoing);
    }

    public void Leave(SessionId sessionId)
    {
        lock (gate)
        {
            if (!bySink.Remove(sessionId, out var lease))
            {
                return;
            }

            lease.Sinks.RemoveAll(sink => sink.SessionId == sessionId);

            if (lease.IsIdle)
            {
                lease.IdleSince = timeProvider.GetUtcNow();
            }
        }
    }

    public void Sweep()
    {
        List<ITunerDevice> closing;

        lock (gate)
        {
            closing = Expire();
        }

        foreach (var device in closing)
        {
            Close(device);
        }
    }

    public void Discard(string deviceId)
    {
        List<ITunerDevice> closing = [];

        lock (gate)
        {
            if (leases.Remove(deviceId, out var lease))
            {
                foreach (var sink in lease.Sinks)
                {
                    bySink.Remove(sink.SessionId);
                }

                Forget(lease, closing);
            }

            if (retiring.Remove(deviceId, out var outgoing))
            {
                closing.Add(outgoing);
            }
        }

        foreach (var device in closing)
        {
            Close(device);
        }
    }

    public bool IsHeld(string deviceId)
    {
        lock (gate)
        {
            return leases.TryGetValue(deviceId, out var lease) && !lease.IsIdle;
        }
    }

    public bool IsLingering(string deviceId)
    {
        lock (gate)
        {
            return leases.TryGetValue(deviceId, out var lease) && lease.IsIdle;
        }
    }

    public IReadOnlyList<SessionId> SinksOn(string deviceId)
    {
        lock (gate)
        {
            return leases.TryGetValue(deviceId, out var lease)
                ? [.. lease.Sinks.Select(sink => sink.SessionId)]
                : [];
        }
    }

    public void Dispose()
    {
        List<ITunerDevice> closing = [];

        lock (gate)
        {
            foreach (var lease in leases.Values)
            {
                Forget(lease, closing);
            }

            leases.Clear();
            bySink.Clear();

            closing.AddRange(retiring.Values);
            retiring.Clear();
        }

        foreach (var device in closing)
        {
            Close(device);
        }
    }

    private IEnumerable<Lease> LeasesFor(PoolRequest request)
    {
        foreach (var deviceId in request.Allowed)
        {
            if (leases.TryGetValue(deviceId, out var lease))
            {
                yield return lease;
            }
        }
    }

    private List<ITunerDevice> Expire()
    {
        var now = timeProvider.GetUtcNow();
        List<ITunerDevice> closing = [];

        foreach (var deviceId in leases.Keys.ToArray())
        {
            var lease = leases[deviceId];

            if (lease.IdleSince is not { } since || now - since < linger)
            {
                continue;
            }

            leases.Remove(deviceId);
            Forget(lease, closing);
        }

        return closing;
    }

    private void Retire(string deviceId, ITunerDevice device)
    {
        if (retiring.Remove(deviceId, out var already))
        {
            Close(already);
        }

        retiring[deviceId] = device;
    }

    private static void Forget(Lease lease, List<ITunerDevice> closing)
    {
        if (lease.Device is { } device)
        {
            closing.Add(device);
        }

        lease.Device = null;
        lease.Ready.Dispose();
    }

    private static void Close(ITunerDevice device)
    {
        try
        {
            device.Dispose();
        }
        catch (Exception)
        {
            return;
        }
    }
}
