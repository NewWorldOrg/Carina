using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Sessions;

public sealed class TunerSessionManager(
    DriverConfiguration configuration,
    ITunerDeviceFactory deviceFactory,
    TimeProvider timeProvider
) : IHostedService
{
    private readonly ConcurrentDictionary<SessionId, TunerSession> sessions = [];

    public IReadOnlyCollection<TunerSession> Sessions => [.. sessions.Values];

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in sessions.Values)
        {
            session.Stop();
        }

        return Task.CompletedTask;
    }

    public TunerSession Begin(
        SessionId sessionId,
        StartSessionRequest request,
        string deviceId,
        DateTimeOffset endsAt
    )
    {
        if (sessionId.IsUnset)
        {
            throw new ArgumentException("A session needs an identifier.", nameof(sessionId));
        }

        var device =
            (configuration.Devices ?? []).FirstOrDefault(candidate => candidate.Id == deviceId)
            ?? throw new ArgumentException(
                $"No device is called '{deviceId}'.",
                nameof(deviceId)
            );

        if (!device.Enabled)
        {
            throw new ArgumentException(
                $"The device '{deviceId}' is disabled.",
                nameof(deviceId)
            );
        }

        if (!Matches(device.Kind, request.Tuning.Kind))
        {
            throw new ArgumentException(
                $"The device '{deviceId}' serves {device.Kind}, and the request asks for {request.Tuning.Kind}.",
                nameof(deviceId)
            );
        }

        if (sessions.ContainsKey(sessionId))
        {
            throw new ArgumentException(
                $"The session '{sessionId}' already exists.",
                nameof(sessionId)
            );
        }

        var now = timeProvider.GetUtcNow();
        var tunerDevice = deviceFactory.Create(device, request.Tuning);

        RecordingWriter? writer = null;
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

            var session = new TunerSession(
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
            writer?.Dispose();
            tunerDevice.Dispose();
            throw;
        }
    }

    public bool TryGet(SessionId sessionId, [NotNullWhen(true)] out TunerSession? session) =>
        sessions.TryGetValue(sessionId, out session);

    public bool Stop(SessionId sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        session.Stop();

        return true;
    }

    private void Forget(TunerSession session) => sessions.TryRemove(session.SessionId, out _);

    private static bool Matches(DeviceKind device, TunerKind requested) =>
        (device, requested) switch
        {
            (DeviceKind.Terrestrial, TunerKind.Terrestrial) => true,
            (DeviceKind.Satellite, TunerKind.Satellite) => true,
            _ => false,
        };
}
