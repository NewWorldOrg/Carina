using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;

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
                session.Resyncs
            ),
        };

    public static IReadOnlyList<SessionSnapshot> All(
        TunerSessionManager manager,
        DriverHello hello
    )
    {
        var seen = new HashSet<SessionId>();
        var snapshots = new List<SessionSnapshot>();

        foreach (var session in Ordered(manager.Sessions).Concat(Ordered(manager.Recent)))
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
        var busy = new Dictionary<string, SessionId>(StringComparer.Ordinal);
        foreach (var session in manager.Sessions)
        {
            busy.TryAdd(session.DeviceId, session.SessionId);
        }

        var snapshots = new List<TunerSnapshot>();

        foreach (var device in configuration.Devices ?? [])
        {
            if (device?.Id is not { } deviceId)
            {
                continue;
            }

            var kind = DeviceViews.Wire(device.Kind);

            if (!manager.IsEnabled(device))
            {
                snapshots.Add(
                    busy.TryGetValue(deviceId, out var draining)
                        ? new TunerSnapshot(
                            deviceId,
                            kind,
                            TunerState.Draining,
                            draining,
                            "This device was turned off and comes out of service as soon as the session it holds ends."
                        )
                        : new TunerSnapshot(
                            deviceId,
                            kind,
                            TunerState.Disabled,
                            Detail: "This device is turned off in the driver configuration."
                        )
                );

                continue;
            }

            if (manager.IsFaulted(deviceId, out var fault))
            {
                snapshots.Add(
                    new TunerSnapshot(deviceId, kind, TunerState.Faulted, Detail: fault)
                );

                continue;
            }

            snapshots.Add(
                busy.TryGetValue(deviceId, out var sessionId)
                    ? new TunerSnapshot(deviceId, kind, TunerState.Busy, sessionId)
                    : new TunerSnapshot(deviceId, kind, TunerState.Idle)
            );
        }

        return snapshots;
    }

    private static IEnumerable<TunerSession> Ordered(IReadOnlyCollection<TunerSession> sessions) =>
        sessions.OrderBy(session => session.StartedAt).ThenBy(session => session.SessionId.Value, StringComparer.Ordinal);
}
