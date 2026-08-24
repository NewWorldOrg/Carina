using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

namespace Carina.Driver.Ipc;

public static class SessionViews
{
    public static SessionSnapshot Of(TunerSession session, DriverHello hello) =>
        new(
            session.SessionId,
            session.Purpose,
            session.DeviceId,
            session.State,
            session.StartedAt,
            session.EndsAt
        )
        {
            StopReason = session.StopReason,
            Concluded = session.Concluded,
            InstanceId = hello.InstanceId,
            OutputRoot = session.OutputRoot,
            RecordingId = session.RecordingId,
            BytesRecorded = session.BytesRecorded,
            FaultCount = session.FaultCount,
            DroppedChunks = session.Broadcaster.DroppedChunks,
            FirstFault = session.FirstFault?.Message,
            FailureCause = session.FailureCause?.Message,
            Counters = new SessionCounters(
                session.Counters.Packets,
                session.Counters.Drops,
                session.Counters.Duplicates,
                session.Counters.Discontinuities,
                session.Counters.TransportErrors,
                session.Counters.ScrambledPackets,
                session.Counters.ProvisionalPackets,
                session.DiscardedBytes,
                session.Resyncs,
                session.DeviceOverflows,
                session.LockLosses
            ),
        };

    public static IReadOnlyList<SessionSnapshot> All(
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        var seen = new HashSet<SessionId>();
        var snapshots = new List<SessionSnapshot>();

        foreach (TunerSession? session in Ordered(manager.Sessions).Concat(Ordered(manager.Recent)))
        {
            if (seen.Add(session.SessionId))
            {
                snapshots.Add(Of(session, hello));
            }
        }

        return snapshots;
    }

    public static IReadOnlyList<TunerSnapshot> Tuners(
        DriverConfiguration configuration,
        TunerSessionManager manager
    )
    {
        var busy = new Dictionary<string, TunerSession>(StringComparer.Ordinal);
        var readings = new Dictionary<string, SignalQualitySample>(StringComparer.Ordinal);

        foreach (TunerSession session in manager.Sessions)
        {
            busy.TryAdd(session.DeviceId, session);

            if (session.Quality is not { } sample)
            {
                continue;
            }

            if (
                !readings.TryGetValue(session.DeviceId, out SignalQualitySample? seen)
                || seen.MeasuredAt < sample.MeasuredAt
            )
            {
                readings[session.DeviceId] = sample;
            }
        }

        var snapshots = new List<TunerSnapshot>();

        foreach (DeviceSettings device in configuration.Devices ?? [])
        {
            if (device?.Id is not { } deviceId)
            {
                continue;
            }

            bool toggled = manager.IsToggled(device);
            TunerSnapshot snapshot = Of(device, deviceId, manager, busy, toggled);

            snapshots.Add(
                snapshot with
                {
                    Toggled = toggled,
                    SignalQuality = readings.TryGetValue(deviceId, out SignalQualitySample? reading)
                        ? SignalQualityViews.Of(reading)
                        : null,
                    CurrentSession = Held(snapshot, busy, deviceId),
                    Health = HealthOf(device, deviceId, manager, snapshot.State),
                }
            );
        }

        return snapshots;
    }

    private static CurrentSessionDto? Held(
        TunerSnapshot snapshot,
        Dictionary<string, TunerSession> busy,
        string deviceId
    ) =>
        busy.TryGetValue(deviceId, out TunerSession? session)
        && snapshot.SessionId.Equals(session.SessionId)
            ? new CurrentSessionDto
            {
                SessionId = session.SessionId,
                Purpose = session.Purpose,
                StartedAt = session.StartedAt,
                Tune = session.Tune,
                EndsAt = session.EndsAt,
            }
            : null;

    private static TunerHealthDto HealthOf(
        DeviceSettings device,
        string deviceId,
        TunerSessionManager manager,
        TunerState state
    ) =>
        new()
        {
            Level = manager.IsFaulted(deviceId, out string? fault)
                ? TunerHealthLevel.Faulted
                : TunerHealthLevel.Healthy,
            DisablePending = state is TunerState.Draining,
            LnbPowered = device.Kind is DeviceKind.Satellite && device.LnbPower,
            Detail = fault,
            ChangedAt = manager.HealthChangedAt(deviceId),
        };

    private static TunerSnapshot Of(
        DeviceSettings device,
        string deviceId,
        TunerSessionManager manager,
        Dictionary<string, TunerSession> busy,
        bool toggled
    )
    {
        TunerKind kind = DeviceViews.Wire(device.Kind);

        if (!manager.IsEnabled(device))
        {
            return busy.TryGetValue(deviceId, out TunerSession? draining)
                ? new TunerSnapshot(
                    deviceId,
                    kind,
                    TunerState.Draining,
                    draining.SessionId,
                    "This device was turned off and comes out of service as soon as the session it holds ends."
                )
                : new TunerSnapshot(
                    deviceId,
                    kind,
                    TunerState.Disabled,
                    Detail: toggled
                        ? "This device was turned off while the driver was running, and a restart puts it back the way the ledger has it."
                        : "This device is turned off in the driver configuration."
                );
        }

        if (manager.IsFaulted(deviceId, out string? fault))
        {
            return new TunerSnapshot(deviceId, kind, TunerState.Faulted, Detail: fault);
        }

        return busy.TryGetValue(deviceId, out TunerSession? session)
            ? new TunerSnapshot(deviceId, kind, TunerState.Busy, session.SessionId)
            : new TunerSnapshot(deviceId, kind, TunerState.Idle);
    }

    private static IEnumerable<TunerSession> Ordered(IReadOnlyCollection<TunerSession> sessions) =>
        sessions.OrderBy(session => session.StartedAt).ThenBy(session => session.SessionId.Value, StringComparer.Ordinal);
}
