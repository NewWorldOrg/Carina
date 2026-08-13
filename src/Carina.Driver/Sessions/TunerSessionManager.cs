using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Recording;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Sessions;

public sealed class TunerSessionManager(
    DriverConfiguration configuration,
    ITunerDeviceFactory deviceFactory,
    TimeProvider timeProvider,
    ILogger<TunerSessionManager> logger,
    TimeSpan? hardStopLimit = null,
    DriverEventHub? events = null
) : IHostedService
{
    public const int RetainedSessions = 64;

    public static readonly TimeSpan DefaultHardStopLimit = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<SessionId, TunerSession> sessions = [];
    private readonly ConcurrentDictionary<string, SessionId> claimedDevices = [];
    private readonly ConcurrentQueue<TunerSession> ended = new();
    private readonly TimeSpan drainCap = TimeSpan.FromHours(
        Math.Max(0, configuration.ShutdownGraceHours)
    );
    private readonly TimeSpan hardStop = hardStopLimit ?? DefaultHardStopLimit;

    private volatile bool draining;

    public IReadOnlyCollection<TunerSession> Sessions => [.. sessions.Values];

    public IReadOnlyCollection<TunerSession> Recent => [.. ended];

    public bool IsDraining => draining;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void EnterDraining()
    {
        draining = true;
        events?.Signal(DriverEvents.Draining);
    }

    public void DetachEverySubscriber()
    {
        foreach (var session in sessions.Values)
        {
            session.Broadcaster.Close(
                new OperationCanceledException(
                    $"The driver is shutting down; the stream of '{session.SessionId}' ends here and is incomplete."
                )
            );
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        EnterDraining();

        var running = sessions.Values.ToArray();
        if (running.Length is 0)
        {
            return;
        }

        var recordings = running
            .Where(session => session.Purpose is SessionPurpose.Recording)
            .ToArray();

        foreach (var session in running.Except(recordings))
        {
            session.Stop();
        }

        var everyone = Task.WhenAll(running.Select(session => session.Completion));

        if (recordings.Length > 0)
        {
            logger.LogInformation(
                "Shutdown was asked for while {Count} recordings were running; staying up for up to {DrainCap}.",
                recordings.Length,
                drainCap
            );

            if (await Settles(everyone, drainCap, cancellationToken))
            {
                return;
            }

            foreach (var session in recordings.Where(session => !session.Completion.IsCompleted))
            {
                logger.LogWarning(
                    "Recording {SessionId} on {DeviceId} is being cut short after {BytesRecorded} bytes because shutdown could not wait any longer.",
                    session.SessionId.Value,
                    session.DeviceId,
                    session.BytesRecorded
                );

                session.Stop(SessionStopReason.DrainCapReached);
            }
        }

        if (await Settles(everyone, hardStop, CancellationToken.None))
        {
            return;
        }

        foreach (var session in running.Where(session => !session.Completion.IsCompleted))
        {
            logger.LogError(
                "Session {SessionId} on {DeviceId} did not let go within {HardStopLimit}; the driver is exiting without it.",
                session.SessionId.Value,
                session.DeviceId,
                hardStop
            );
        }
    }

    private static async Task<bool> Settles(
        Task everyone,
        TimeSpan limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await everyone.WaitAsync(limit, cancellationToken);

            return true;
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    public SessionStart Begin(StartSessionRequest request)
    {
        var now = timeProvider.GetUtcNow();

        var problems = request.Validate(now);
        if (problems.Count > 0)
        {
            return SessionStart.Refused(SessionRefusal.Rejected, string.Join(" ", problems));
        }

        if (draining)
        {
            return SessionStart.Refused(
                SessionRefusal.Draining,
                "The driver is shutting down, so no session can start."
            );
        }

        if (TryGet(request.SessionId, out _))
        {
            return SessionStart.Refused(
                SessionRefusal.DuplicateSession,
                $"The session '{request.SessionId}' already exists."
            );
        }

        if (!TryResolveOutput(request, out var directory, out var outputRefusal))
        {
            return outputRefusal;
        }

        if (!TryClaimDevice(request, out var device, out var deviceRefusal))
        {
            return deviceRefusal;
        }

        return Open(request, device, directory, now, EndOf(request, now));
    }

    private DateTimeOffset EndOf(StartSessionRequest request, DateTimeOffset now) =>
        request.EndsAt ?? now.AddMinutes(configuration.LiveSessionMinutes);

    private bool TryResolveOutput(
        StartSessionRequest request,
        out string? directory,
        [NotNullWhen(false)] out SessionStart? refusal
    )
    {
        directory = null;
        refusal = null;

        if (request.Purpose is not SessionPurpose.Recording)
        {
            return true;
        }

        if (configuration.TryResolveOutputRoot(request.OutputRoot, out var resolved))
        {
            directory = resolved;

            return true;
        }

        var declared = string.Join(
            ", ",
            (configuration.OutputRoots ?? []).Select(root => root.Name)
        );

        refusal = SessionStart.Refused(
            SessionRefusal.UnknownOutputRoot,
            $"This driver declares no output root called '{request.OutputRoot}'; it declares {declared}."
        );

        return false;
    }

    private bool TryClaimDevice(
        StartSessionRequest request,
        [NotNullWhen(true)] out DeviceSettings? device,
        [NotNullWhen(false)] out SessionStart? refusal
    )
    {
        device = null;
        refusal = null;

        var declared = configuration.Devices ?? [];

        if (request.DeviceId is { } named)
        {
            var candidate = declared.FirstOrDefault(entry => entry.Id == named);

            if (candidate is null)
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.UnknownDevice,
                    $"No device is called '{named}'."
                );

                return false;
            }

            if (!candidate.Enabled)
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.DisabledDevice,
                    $"The device '{named}' is disabled."
                );

                return false;
            }

            if (!Matches(candidate.Kind, request.Tuning.Kind))
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.WrongDeviceKind,
                    $"The device '{named}' serves {candidate.Kind}, and the request asks for {request.Tuning.Kind}."
                );

                return false;
            }

            if (!claimedDevices.TryAdd(named, request.SessionId))
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.DeviceBusy,
                    $"The device '{named}' is already serving a session."
                );

                return false;
            }

            device = candidate;

            return true;
        }

        var usable = declared
            .Where(entry => entry.Enabled && Matches(entry.Kind, request.Tuning.Kind))
            .ToArray();

        if (usable.Length is 0)
        {
            refusal = SessionStart.Refused(
                SessionRefusal.NoDeviceOfThatKind,
                $"This driver has no enabled device that serves {request.Tuning.Kind}."
            );

            return false;
        }

        foreach (var candidate in usable)
        {
            if (claimedDevices.TryAdd(candidate.Id!, request.SessionId))
            {
                device = candidate;

                return true;
            }
        }

        refusal = SessionStart.Refused(
            SessionRefusal.NoDeviceFree,
            $"Every device that serves {request.Tuning.Kind} is already serving a session."
        );

        return false;
    }

    private SessionStart Open(
        StartSessionRequest request,
        DeviceSettings device,
        string? directory,
        DateTimeOffset now,
        DateTimeOffset endsAt
    )
    {
        var deviceId = device.Id!;
        var sessionId = request.SessionId;

        ITunerDevice tunerDevice;
        try
        {
            tunerDevice = deviceFactory.Create(device, request.Tuning);
        }
        catch (Exception error)
        {
            Release(deviceId, sessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The device '{deviceId}' could not be opened: {error.Message}"
            );
        }

        RecordingWriter? writer = null;
        if (directory is not null)
        {
            var refusal = TryOpenRecording(directory, sessionId, deviceId, out writer);
            if (refusal is not null)
            {
                tunerDevice.Dispose();

                return refusal;
            }
        }

        var session = new TunerSession(
            sessionId,
            request.Purpose,
            deviceId,
            tunerDevice,
            now,
            endsAt,
            timeProvider,
            writer,
            logger: logger,
            outputRoot: request.OutputRoot
        );

        if (!sessions.TryAdd(sessionId, session))
        {
            Release(deviceId, sessionId);
            session.Dispose();

            return SessionStart.Refused(
                SessionRefusal.DuplicateSession,
                $"The session '{sessionId}' already exists."
            );
        }

        session.Ended += Forget;

        try
        {
            session.Start();
        }
        catch (Exception error)
        {
            sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(sessionId, session));
            session.Ended -= Forget;
            Release(deviceId, sessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The session '{sessionId}' could not be started: {error.Message}"
            );
        }

        Announce();

        return SessionStart.Started(session);
    }

    private SessionStart? TryOpenRecording(
        string directory,
        SessionId sessionId,
        string deviceId,
        out RecordingWriter? writer
    )
    {
        writer = null;

        try
        {
            writer = new RecordingWriter(directory, sessionId);

            return null;
        }
        catch (IOException error) when (File.Exists(Path.Combine(directory, $"{sessionId}.ts")))
        {
            Release(deviceId, sessionId);

            return SessionStart.Refused(
                SessionRefusal.RecordingAlreadyExists,
                $"A recording for '{sessionId}' is already on disk, and this driver never appends to one: {error.Message}"
            );
        }
        catch (Exception error)
        {
            Release(deviceId, sessionId);

            return SessionStart.Refused(
                SessionRefusal.OutputUnavailable,
                $"The recording for '{sessionId}' could not be opened: {error.Message}"
            );
        }
    }

    public bool TryGet(SessionId sessionId, [NotNullWhen(true)] out TunerSession? session)
    {
        if (sessions.TryGetValue(sessionId, out session))
        {
            return true;
        }

        session = ended.FirstOrDefault(candidate => candidate.SessionId == sessionId);

        return session is not null;
    }

    public SessionStopOutcome Stop(SessionId sessionId)
    {
        if (sessions.TryGetValue(sessionId, out var session))
        {
            session.Stop();

            return SessionStopOutcome.Stopping;
        }

        return ended.Any(candidate => candidate.SessionId == sessionId)
            ? SessionStopOutcome.AlreadyEnded
            : SessionStopOutcome.NoSuchSession;
    }

    private void Release(string deviceId, SessionId sessionId) =>
        claimedDevices.TryRemove(new KeyValuePair<string, SessionId>(deviceId, sessionId));

    private void Forget(TunerSession session)
    {
        sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(session.SessionId, session));
        Release(session.DeviceId, session.SessionId);
        ended.Enqueue(session);

        while (ended.Count > RetainedSessions && ended.TryDequeue(out _))
        { }

        Announce();
    }

    private void Announce()
    {
        events?.Signal(DriverEvents.Sessions);
        events?.Signal(DriverEvents.Tuners);
    }

    public bool IsClaimed(string deviceId) => claimedDevices.ContainsKey(deviceId);

    private static bool Matches(DeviceKind device, TunerKind requested) =>
        (device, requested) switch
        {
            (DeviceKind.Terrestrial, TunerKind.Terrestrial) => true,
            (DeviceKind.Satellite, TunerKind.Satellite) => true,
            _ => false,
        };
}
