using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Driver.Configuration;
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
    TimeSpan? hardStopLimit = null
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

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        draining = true;

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

    public TunerSession Begin(
        SessionId sessionId,
        StartSessionRequest request,
        string deviceId,
        DateTimeOffset endsAt
    )
    {
        if (draining)
        {
            throw new InvalidOperationException(
                "The driver is shutting down, so no session can start."
            );
        }

        if (sessionId.IsUnset)
        {
            throw new ArgumentException("A session needs an identifier.", nameof(sessionId));
        }

        if (request.EndsAt is { } requested && requested != endsAt)
        {
            throw new ArgumentException(
                $"The request ends at {requested:O} and the session was told to end at {endsAt:O}.",
                nameof(endsAt)
            );
        }

        var device =
            (configuration.Devices ?? []).FirstOrDefault(candidate => candidate.Id == deviceId)
            ?? throw new ArgumentException($"No device is called '{deviceId}'.", nameof(deviceId));

        if (!device.Enabled)
        {
            throw new ArgumentException($"The device '{deviceId}' is disabled.", nameof(deviceId));
        }

        if (!Matches(device.Kind, request.Tuning.Kind))
        {
            throw new ArgumentException(
                $"The device '{deviceId}' serves {device.Kind}, and the request asks for {request.Tuning.Kind}.",
                nameof(deviceId)
            );
        }

        if (TryGet(sessionId, out _))
        {
            throw new ArgumentException(
                $"The session '{sessionId}' already exists.",
                nameof(sessionId)
            );
        }

        if (!claimedDevices.TryAdd(deviceId, sessionId))
        {
            throw new ArgumentException(
                $"The device '{deviceId}' is already serving a session.",
                nameof(deviceId)
            );
        }

        var now = timeProvider.GetUtcNow();
        ITunerDevice? tunerDevice = null;
        RecordingWriter? writer = null;
        TunerSession? session = null;

        try
        {
            tunerDevice = deviceFactory.Create(device, request.Tuning);

            if (request.Purpose is SessionPurpose.Recording)
            {
                writer = new RecordingWriter(
                    configuration.RecordingsDirectory
                        ?? throw new InvalidOperationException(
                            "A recording needs a recordings directory, and the configuration has none."
                        ),
                    sessionId
                );
            }

            session = new TunerSession(
                sessionId,
                request.Purpose,
                deviceId,
                tunerDevice,
                now,
                endsAt,
                timeProvider,
                writer,
                logger: logger
            );

            if (!sessions.TryAdd(sessionId, session))
            {
                throw new ArgumentException(
                    $"The session '{sessionId}' already exists.",
                    nameof(sessionId)
                );
            }

            session.Ended += Forget;
            session.Start();

            return session;
        }
        catch
        {
            claimedDevices.TryRemove(new KeyValuePair<string, SessionId>(deviceId, sessionId));

            if (session is not null)
            {
                sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(sessionId, session));
                session.Ended -= Forget;
                session.Dispose();
            }
            else
            {
                writer?.Dispose();
                tunerDevice?.Dispose();
            }

            throw;
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

    public bool Stop(SessionId sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        session.Stop();

        return true;
    }

    private void Forget(TunerSession session)
    {
        sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(session.SessionId, session));
        claimedDevices.TryRemove(
            new KeyValuePair<string, SessionId>(session.DeviceId, session.SessionId)
        );
        ended.Enqueue(session);

        while (ended.Count > RetainedSessions && ended.TryDequeue(out _))
        { }
    }


    private static bool Matches(DeviceKind device, TunerKind requested) =>
        (device, requested) switch
        {
            (DeviceKind.Terrestrial, TunerKind.Terrestrial) => true,
            (DeviceKind.Satellite, TunerKind.Satellite) => true,
            _ => false,
        };
}
