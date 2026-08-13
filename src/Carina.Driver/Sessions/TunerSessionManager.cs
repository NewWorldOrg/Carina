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
    ILogger<TunerSessionManager> logger
) : IHostedService
{
    public const int RetainedSessions = 64;

    private readonly ConcurrentDictionary<SessionId, TunerSession> sessions = [];
    private readonly ConcurrentQueue<TunerSession> ended = new();
    private readonly TimeSpan drainCap = TimeSpan.FromHours(configuration.ShutdownGraceHours);

    private volatile bool draining;

    public IReadOnlyCollection<TunerSession> Sessions => [.. sessions.Values];

    public IReadOnlyCollection<TunerSession> Recent => [.. ended];

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        draining = true;

        var running = sessions.Values.ToArray();
        var recordings = running
            .Where(session => session.Purpose is SessionPurpose.Recording)
            .ToArray();

        foreach (var session in running.Except(recordings))
        {
            session.Stop();
        }

        if (recordings.Length is 0)
        {
            await Task.WhenAll(running.Select(session => session.Completion))
                .WaitAsync(cancellationToken);

            return;
        }

        logger.LogInformation(
            "Shutdown was asked for while {Count} recordings were running; staying up for up to {DrainCap}.",
            recordings.Length,
            drainCap
        );

        var everyone = Task.WhenAll(running.Select(session => session.Completion));

        try
        {
            await everyone.WaitAsync(drainCap, cancellationToken);
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning(
                error,
                "The recordings did not finish within the grace period, so they are being stopped."
            );

            foreach (var session in recordings)
            {
                session.Stop(SessionStopReason.DrainCapReached);
            }

            await everyone;
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

        if (sessions.Values.Any(existing => existing.DeviceId == deviceId))
        {
            throw new ArgumentException(
                $"The device '{deviceId}' is already serving a session.",
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

        var now = timeProvider.GetUtcNow();
        var tunerDevice = deviceFactory.Create(device, request.Tuning);

        RecordingWriter? writer = null;
        TunerSession? session = null;

        try
        {
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
                writer
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
            if (session is not null)
            {
                sessions.TryRemove(sessionId, out _);
                session.Ended -= Forget;
            }

            writer?.Dispose();
            tunerDevice.Dispose();

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
        sessions.TryRemove(session.SessionId, out _);
        ended.Enqueue(session);

        while (ended.Count > RetainedSessions && ended.TryDequeue(out _))
        { }

        Report(session);
    }

    private void Report(TunerSession session)
    {
        if (session.State is SessionState.Failed)
        {
            logger.LogError(
                session.FailureCause,
                "Session {SessionId} on {DeviceId} failed after {BytesRecorded} bytes.",
                session.SessionId.Value,
                session.DeviceId,
                session.BytesRecorded
            );
        }
        else
        {
            logger.LogInformation(
                "Session {SessionId} on {DeviceId} ended ({StopReason}) after {BytesRecorded} bytes.",
                session.SessionId.Value,
                session.DeviceId,
                session.StopReason,
                session.BytesRecorded
            );
        }

        if (session.FaultCount > 0)
        {
            logger.LogWarning(
                session.FirstFault,
                "Session {SessionId} met {FaultCount} faults that did not stop it.",
                session.SessionId.Value,
                session.FaultCount
            );
        }
    }

    private static bool Matches(DeviceKind device, TunerKind requested) =>
        (device, requested) switch
        {
            (DeviceKind.Terrestrial, TunerKind.Terrestrial) => true,
            (DeviceKind.Satellite, TunerKind.Satellite) => true,
            _ => false,
        };
}
