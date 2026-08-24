using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Diagnostics;
using Carina.Driver.Events;
using Carina.Driver.Recording;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Sessions;

public sealed class TunerSessionManager(
    DriverConfiguration configuration,
    ITunerDeviceFactory deviceFactory,
    TimeProvider timeProvider,
    ILogger<TunerSessionManager> logger,
    TimeSpan? hardStopLimit = null,
    DriverEventHub? events = null,
    DiagnosticsStore? diagnostics = null,
    IRecordingWriterFactory? recordingWriters = null,
    TimeSpan? tunerGrace = null,
    TimeSpan? letGoLimit = null
) : IHostedService
{
    public const int RetainedSessions = 64;

    public const int RepeatedTuneFailureCeiling = 3;

    public static readonly TimeSpan DefaultHardStopLimit = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan HandOverLimit = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan LetGoLimit =
        DvbTunerSettings.Default.BytePatience + TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<SessionId, TunerSession> sessions = [];
    private readonly TunerPool pool = new(timeProvider, tunerGrace);
    private readonly ConcurrentDictionary<string, string> faultedDevices = new(
        StringComparer.Ordinal
    );
    private readonly ConcurrentDictionary<string, bool> toggledDevices = new(
        StringComparer.Ordinal
    );
    private readonly ConcurrentDictionary<string, DateTimeOffset> healthChangedAt = new(
        StringComparer.Ordinal
    );
    private readonly ConcurrentQueue<TunerSession> ended = new();
    private readonly ConcurrentDictionary<SessionId, TuningKey> tunings = [];
    private readonly Dictionary<string, Dictionary<TuningKey, int>> tuneFailureStreaks = new(
        StringComparer.Ordinal
    );
    private readonly Lock streakGate = new();
    private readonly TimeSpan drainCap = TimeSpan.FromHours(
        Math.Max(0, configuration.ShutdownGraceHours)
    );
    private readonly TimeSpan hardStop = hardStopLimit ?? DefaultHardStopLimit;
    private readonly TimeSpan letGo = letGoLimit ?? LetGoLimit;
    private readonly IRecordingWriterFactory writerFactory =
        recordingWriters ?? new RecordingWriterFactory();

    private readonly Lock drainGate = new();

    private volatile bool draining;
    private Task? drain;

    public IReadOnlyCollection<TunerSession> Sessions => [.. sessions.Values];

    public IReadOnlyCollection<TunerSession> Recent => [.. ended];

    public bool IsDraining => draining;

    public TimeSpan ShutdownBudget => drainCap + hardStop;

    public TimeSpan HardStopBudget => hardStop;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void EnterDraining()
    {
        lock (drainGate)
        {
            EnterDrainingUnderGate();
        }
    }

    public bool TryEnterDrainingUnlessRecording(out IReadOnlyList<TunerSession> recordings)
    {
        lock (drainGate)
        {
            TunerSession[] held = sessions
                .Values.Where(session => session.Purpose is SessionPurpose.Recording)
                .ToArray();

            if (held.Length > 0)
            {
                recordings = held;

                return false;
            }

            EnterDrainingUnderGate();
            recordings = [];

            return true;
        }
    }

    private void EnterDrainingUnderGate()
    {
        if (draining)
        {
            return;
        }

        draining = true;
        events?.Signal(DriverEvents.Draining);
    }

    public void DetachEverySubscriber()
    {
        foreach (TunerSession session in sessions.Values)
        {
            session.Broadcaster.Close(
                new OperationCanceledException(
                    $"The driver is shutting down; the stream of '{session.SessionId}' ends here and is incomplete."
                ),
                SessionStopReason.Requested
            );
        }
    }

    public Task DrainAsync(CancellationToken cancellationToken)
    {
        lock (drainGate)
        {
            drain ??= Drain(cancellationToken);

            return drain;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => DrainAsync(cancellationToken);

    private async Task Drain(CancellationToken cancellationToken)
    {
        TunerSession[] running;

        lock (drainGate)
        {
            EnterDrainingUnderGate();
            running = [.. sessions.Values];
        }

        if (running.Length is 0)
        {
            pool.Dispose();

            return;
        }

        TunerSession[] recordings = running
            .Where(session => session.Purpose is SessionPurpose.Recording)
            .ToArray();

        foreach (TunerSession? session in running.Except(recordings))
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

            var theRecordings = Task.WhenAll(recordings.Select(session => session.Completion));

            if (await Settles(theRecordings, drainCap, cancellationToken))
            {
                if (!await Settles(everyone, hardStop, CancellationToken.None))
                {
                    GiveUpOn(running);
                }

                pool.Dispose();

                return;
            }

            foreach (TunerSession? session in recordings.Where(session => !session.Completion.IsCompleted))
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

        if (!await Settles(everyone, hardStop, CancellationToken.None))
        {
            GiveUpOn(running);
        }

        pool.Dispose();
    }

    private void GiveUpOn(TunerSession[] running)
    {
        foreach (TunerSession? session in running.Where(session => !session.Completion.IsCompleted))
        {
            logger.LogError(
                "Session {SessionId} on {DeviceId} did not let go within {HardStopLimit}; the driver is exiting without it.",
                session.SessionId.Value,
                session.DeviceId,
                hardStop
            );
        }
    }

    private async Task<bool> Settles(
        Task everyone,
        TimeSpan limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await everyone.WaitAsync(limit, timeProvider, cancellationToken);

            return true;
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    public SessionStart Begin(StartSessionRequest request)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        IReadOnlyList<string> problems = request.Validate(now);
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

        if (!TryResolveOutput(request, out string? directory, out SessionStart? outputRefusal))
        {
            return outputRefusal;
        }

        if (!TryEligibleDevices(request, out IReadOnlyList<string>? candidates, out SessionStart? deviceRefusal))
        {
            return deviceRefusal;
        }

        PoolGrant grant = pool.Acquire(
            new PoolRequest(
                request.SessionId,
                request.Purpose,
                TuningKey.Of(request),
                request.DeviceId,
                candidates
            )
        );

        if (!grant.IsGranted)
        {
            return SessionStart.Refused(
                grant.Verdict is PoolVerdict.DeviceBusy
                    ? SessionRefusal.DeviceBusy
                    : SessionRefusal.NoDeviceFree,
                grant.Detail
            );
        }

        DateTimeOffset endsAt = EndOf(request, now);

        return grant.Verdict is PoolVerdict.Shared
            ? RideAlong(request, grant, directory, now, endsAt)
            : TakeTheTuner(request, grant, directory, now, endsAt);
    }

    private DateTimeOffset EndOf(StartSessionRequest request, DateTimeOffset now)
    {
        if (!SessionPurposes.ReadsEveryPacket(request.Purpose))
        {
            return request.EndsAt ?? now.AddMinutes(configuration.LiveSessionMinutes);
        }

        DateTimeOffset latest = now.AddMinutes(configuration.WalkSessionMinutes);

        if (request.EndsAt is { } asked && asked < latest)
        {
            return asked;
        }

        if (request.EndsAt is { } cut)
        {
            logger.LogInformation(
                "Session {SessionId} asked to run until {Asked} and was cut to {Granted}.",
                request.SessionId.Value,
                cut,
                latest
            );
        }

        return latest;
    }

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

        if (configuration.TryResolveOutputRoot(request.OutputRoot, out string? resolved))
        {
            directory = resolved;

            return true;
        }

        string declared = string.Join(
            ", ",
            (configuration.OutputRoots ?? []).Select(root => root.Name)
        );

        refusal = SessionStart.Refused(
            SessionRefusal.UnknownOutputRoot,
            $"This driver declares no output root called '{request.OutputRoot}'; it declares {declared}."
        );

        return false;
    }

    private bool TryEligibleDevices(
        StartSessionRequest request,
        [NotNullWhen(true)] out IReadOnlyList<string>? candidates,
        [NotNullWhen(false)] out SessionStart? refusal
    )
    {
        candidates = null;
        refusal = null;

        IReadOnlyList<DeviceSettings> declared = configuration.Devices ?? [];

        if (request.DeviceId is { } named)
        {
            DeviceSettings? candidate = declared.FirstOrDefault(entry => entry.Id == named);

            if (candidate is null)
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.UnknownDevice,
                    $"No device is called '{named}'."
                );

                return false;
            }

            if (!IsEnabled(candidate))
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.DisabledDevice,
                    IsClaimed(named)
                        ? $"The device '{named}' is being taken out of service and is finishing the session it holds."
                        : $"The device '{named}' is disabled."
                );

                return false;
            }

            if (faultedDevices.TryGetValue(named, out string? fault))
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.FaultedDevice,
                    $"The device '{named}' is faulted and is not handed out until the driver restarts: {fault}"
                );

                return false;
            }

            if (!Matches(candidate.Kind, KindOf(request)))
            {
                refusal = SessionStart.Refused(
                    SessionRefusal.WrongDeviceKind,
                    $"The device '{named}' serves {candidate.Kind}, and the request asks for {KindOf(request)}."
                );

                return false;
            }

            candidates = [named];

            return true;
        }

        DeviceSettings[] usable = declared
            .Where(entry => IsEnabled(entry) && Matches(entry.Kind, KindOf(request)))
            .ToArray();

        if (usable.Length is 0)
        {
            refusal = SessionStart.Refused(
                SessionRefusal.NoDeviceOfThatKind,
                $"This driver has no enabled device that serves {KindOf(request)}."
            );

            return false;
        }

        DeviceSettings[] healthy = usable
            .Where(entry => !faultedDevices.ContainsKey(entry.Id!))
            .ToArray();

        if (healthy.Length is 0)
        {
            refusal = SessionStart.Refused(
                SessionRefusal.FaultedDevice,
                $"Every device that serves {KindOf(request)} is faulted."
            );

            return false;
        }

        candidates = [.. healthy.Select(entry => entry.Id!)];

        return true;
    }

    private SessionStart TakeTheTuner(
        StartSessionRequest request,
        PoolGrant grant,
        string? directory,
        DateTimeOffset now,
        DateTimeOffset endsAt
    )
    {
        string deviceId = grant.DeviceId;
        SessionId sessionId = request.SessionId;

        if (!HandOver(grant))
        {
            pool.Leave(sessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceBusy,
                $"The device '{deviceId}' was asked for '{sessionId}', and what was on it did not let go within {HandOverLimit}."
            );
        }

        if (!TryTune(request, grant, out ITunerDevice? tuner, out SessionStart? tuneRefusal))
        {
            return tuneRefusal;
        }

        return Open(
            request,
            deviceId,
            tuner,
            directory,
            now,
            endsAt,
            holds: true,
            tuned: grant.NeedsTuning
        );
    }

    private bool HandOver(PoolGrant grant)
    {
        var losers = new List<TunerSession>();

        foreach (SessionId displaced in grant.Displaced)
        {
            if (!sessions.TryGetValue(displaced, out TunerSession? loser))
            {
                continue;
            }

            logger.LogWarning(
                "Session {SessionId} on {DeviceId} is being cut off: {Detail}",
                loser.SessionId.Value,
                loser.DeviceId,
                grant.Detail
            );

            loser.Preempt(grant.Detail);
            losers.Add(loser);
        }

        foreach (TunerSession loser in losers)
        {
            loser.WaitForEnd(HandOverLimit);

            if (!loser.Concluded)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryTune(
        StartSessionRequest request,
        PoolGrant grant,
        [NotNullWhen(true)] out ITunerDevice? tuner,
        [NotNullWhen(false)] out SessionStart? refusal
    )
    {
        string deviceId = grant.DeviceId;
        tuner = null;
        refusal = null;

        if (!grant.NeedsTuning)
        {
            if (pool.DeviceOf(deviceId) is { } held)
            {
                tuner = new LeasedTunerDevice(held);

                return true;
            }

            pool.Leave(request.SessionId);

            refusal = SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The device '{deviceId}' was let go before '{request.SessionId}' could take it over."
            );

            return false;
        }

        pool.HandOver(deviceId);

        DeviceSettings settings = (configuration.Devices ?? []).First(entry =>
            string.Equals(entry?.Id, deviceId, StringComparison.Ordinal)
        )!;

        try
        {
            ITunerDevice opened = deviceFactory.Create(settings, request.Tuning, request.Tune);
            pool.Tuned(deviceId, opened);
            tuner = new LeasedTunerDevice(opened);

            return true;
        }
        catch (Exception error)
        {
            pool.TuningFailed(deviceId, error);

            if (
                error is DvbDeviceException
                {
                    Failure: TuningFailure.NoLock or TuningFailure.LockedWithoutData
                }
            )
            {
                RecordTuneFailure(deviceId, TuningKey.Of(request), error.Message);
            }

            refusal = error is DvbDeviceException { Failure: TuningFailure.NoLock }
                ? SessionStart.Refused(
                    SessionRefusal.NoLock,
                    $"The device '{deviceId}' opened but the frontend did not lock: {error.Message}"
                )
                : SessionStart.Refused(
                    SessionRefusal.DeviceUnavailable,
                    $"The device '{deviceId}' could not be opened: {error.Message}"
                );

            return false;
        }
    }

    private SessionStart RideAlong(
        StartSessionRequest request,
        PoolGrant grant,
        string? directory,
        DateTimeOffset now,
        DateTimeOffset endsAt
    )
    {
        if (
            !pool.AwaitReady(grant.DeviceId, HandOverLimit)
            || !sessions.TryGetValue(grant.Holder, out TunerSession? host)
        )
        {
            pool.Leave(request.SessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The session '{grant.Holder}' that '{request.SessionId}' would have ridden on the device '{grant.DeviceId}' is not reading it."
            );
        }

        if (host.EndsAt <= now)
        {
            pool.Leave(request.SessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The session '{host.SessionId}' that '{request.SessionId}' would read the device '{grant.DeviceId}' through stops at {host.EndsAt:O}, so there is no window left to share."
            );
        }

        SubscriberKind kind = request.Purpose is SessionPurpose.Recording
            ? SubscriberKind.Recording
            : SubscriberKind.Piggyback;

        if (!host.Broadcaster.TrySubscribe(kind, out SessionSubscription? seat))
        {
            pool.Leave(request.SessionId);

            return SessionStart.Refused(
                SessionRefusal.DeviceBusy,
                $"The session '{host.SessionId}' on the device '{grant.DeviceId}' carries {host.Broadcaster.SubscriberLimit} readers at a time and they are all taken."
            );
        }

        return Open(
            request,
            grant.DeviceId,
            new PiggybackTunerDevice(host, seat),
            directory,
            now,
            endsAt > host.EndsAt ? host.EndsAt : endsAt,
            holds: false,
            tuned: false,
            ridesOn: host,
            seat: seat
        );
    }

    private SessionStart Open(
        StartSessionRequest request,
        string deviceId,
        ITunerDevice tunerDevice,
        string? directory,
        DateTimeOffset now,
        DateTimeOffset endsAt,
        bool holds,
        bool tuned,
        TunerSession? ridesOn = null,
        SessionSubscription? seat = null
    )
    {
        SessionId sessionId = request.SessionId;
        TunerSession session;

        lock (drainGate)
        {
            if (draining)
            {
                pool.Leave(sessionId);
                tunerDevice.Dispose();

                return SessionStart.Refused(
                    SessionRefusal.Draining,
                    "The driver is shutting down, so no session can start."
                );
            }

            if (AlreadyWriting(request.RecordingId) is { } holder)
            {
                pool.Leave(sessionId);
                tunerDevice.Dispose();

                return SessionStart.Refused(
                    SessionRefusal.RecordingAlreadyExists,
                    $"The session '{holder}' is writing the recording '{request.RecordingId}', and two writers on one file would interleave."
                );
            }

            IRecordingWriter? writer = null;
            if (directory is not null)
            {
                SessionStart? refusal = TryOpenRecording(
                    directory,
                    sessionId,
                    request.OutputRoot!,
                    request.RecordingId!,
                    out writer
                );

                if (refusal is not null)
                {
                    tunerDevice.Dispose();

                    return refusal;
                }
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
                logger: logger,
                outputRoot: request.OutputRoot,
                recordingId: request.RecordingId,
                diagnostics: diagnostics,
                watch: Watch(request.Purpose),
                tune: request.Tune,
                ridesOn: ridesOn,
                seat: seat,
                demuxBufferBytes: configuration.Tuner?.DemuxBufferBytes
                    ?? TunerSettings.DefaultDemuxBufferBytes
            );

            if (!sessions.TryAdd(sessionId, session))
            {
                pool.Leave(sessionId);
                session.Dispose();

                return SessionStart.Refused(
                    SessionRefusal.DuplicateSession,
                    $"The session '{sessionId}' already exists."
                );
            }

            tunings[sessionId] = TuningKey.Of(request);
        }

        session.Ended += Forget;

        try
        {
            session.Start();
        }
        catch (Exception error)
        {
            sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(sessionId, session));
            tunings.TryRemove(sessionId, out _);
            session.Ended -= Forget;
            pool.Leave(sessionId);
            session.Dispose();

            return SessionStart.Refused(
                SessionRefusal.DeviceUnavailable,
                $"The session '{sessionId}' could not be started: {error.Message}"
            );
        }

        if (holds)
        {
            pool.Ready(deviceId);
        }

        if (tuned)
        {
            events?.Signal(DriverEvents.SessionTuned);
        }

        Announce();

        return SessionStart.Started(session);
    }

    private SessionId? AlreadyWriting(string? recordingId)
    {
        if (recordingId is null)
        {
            return null;
        }

        foreach (TunerSession candidate in sessions.Values)
        {
            if (string.Equals(candidate.RecordingId, recordingId, StringComparison.Ordinal))
            {
                return candidate.SessionId;
            }
        }

        return null;
    }

    private SessionStart? TryOpenRecording(
        string directory,
        SessionId sessionId,
        string outputRoot,
        string recordingId,
        out IRecordingWriter? writer
    )
    {
        writer = null;

        try
        {
            writer = writerFactory.Open(directory, recordingId);

            return null;
        }
        catch (Exception error)
        {
            pool.Leave(sessionId);

            logger.LogError(
                error,
                "The recording {RecordingId} for {SessionId} could not be opened under {OutputRoot}.",
                recordingId,
                sessionId.Value,
                outputRoot
            );

            return SessionStart.Refused(
                SessionRefusal.OutputUnavailable,
                $"The recording '{recordingId}' could not be opened under the output root '{outputRoot}'; the driver log says why."
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

    public SessionExtension Extend(SessionId sessionId, ExtendSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!sessions.TryGetValue(sessionId, out TunerSession? session))
        {
            return ended.Any(candidate => candidate.SessionId == sessionId)
                ? SessionExtension.Refused(
                    SessionExtendOutcome.AlreadyEnded,
                    $"The session '{sessionId}' has already ended, so there is no end left to move."
                )
                : SessionExtension.Refused(
                    SessionExtendOutcome.NoSuchSession,
                    $"This driver holds no session called '{sessionId}'."
                );
        }

        if (session.Purpose is not SessionPurpose.Recording)
        {
            return SessionExtension.Refused(
                SessionExtendOutcome.NotARecording,
                $"The session '{sessionId}' is a {SessionPurposeConverter.WireName(session.Purpose)} one, and this driver holds its end to the limit its purpose is given."
            );
        }

        IReadOnlyList<string> problems = request.Validate(
            session.EndsAt,
            timeProvider.GetUtcNow()
        );

        if (problems.Count > 0)
        {
            return SessionExtension.Refused(
                SessionExtendOutcome.NotAnExtension,
                string.Join(" ", problems)
            );
        }

        if (session.RidesOn is { } host && request.EndsAt > host.EndsAt)
        {
            return SessionExtension.Refused(
                SessionExtendOutcome.NotAnExtension,
                $"endsAt: '{sessionId}' reads the tuner through '{host.SessionId}', which stops at {host.EndsAt:O}, so it cannot be held open until {request.EndsAt:O}."
            );
        }

        if (!session.Extend(request.EndsAt))
        {
            return SessionExtension.Refused(
                SessionExtendOutcome.AlreadyEnded,
                $"The session '{sessionId}' is {session.State} and is no longer taking a later end."
            );
        }

        logger.LogInformation(
            "Session {SessionId} now runs until {EndsAt}.",
            sessionId.Value,
            session.EndsAt
        );

        events?.Signal(DriverEvents.Sessions);

        return SessionExtension.Extended(session);
    }

    public async Task<SessionStopOutcome> StopAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken
    )
    {
        if (!sessions.TryGetValue(sessionId, out TunerSession? session))
        {
            return ended.Any(candidate => candidate.SessionId == sessionId)
                ? SessionStopOutcome.AlreadyEnded
                : SessionStopOutcome.NoSuchSession;
        }

        logger.LogInformation(
            "Session {SessionId} was asked to stop: {Reason}",
            sessionId.Value,
            reason
        );
        session.Stop();

        try
        {
            await session.Completion.WaitAsync(letGo, timeProvider, cancellationToken);

            return SessionStopOutcome.Stopped;
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        {
            return SessionStopOutcome.Stopping;
        }
    }

    private void Forget(TunerSession session)
    {
        sessions.TryRemove(new KeyValuePair<SessionId, TunerSession>(session.SessionId, session));

        if (tunings.TryRemove(session.SessionId, out TuningKey? tuning))
        {
            if (session.State is SessionState.Stopped)
            {
                ForgiveTuneFailures(session.DeviceId);
            }
            else if (
                session.FailureCause is DvbDeviceException
                {
                    Failure: TuningFailure.NoLock or TuningFailure.LockedWithoutData
                } channel
            )
            {
                RecordTuneFailure(session.DeviceId, tuning, channel.Message);
            }
        }

        if (session.StopReason is SessionStopReason.DeviceFailed)
        {
            faultedDevices[session.DeviceId] =
                $"The device failed while serving '{session.SessionId}': "
                + (session.FailureCause?.Message ?? "no cause was recorded.");
            healthChangedAt[session.DeviceId] = timeProvider.GetUtcNow();

            pool.Discard(session.DeviceId);
        }

        pool.Leave(session.SessionId);
        pool.Sweep();

        ended.Enqueue(session);

        while (ended.Count > RetainedSessions && ended.TryDequeue(out _))
        { }

        Announce();
    }

    private SignalQualityWatch Watch(SessionPurpose purpose) =>
        new(
            Interval(purpose),
            (_, _) =>
            {
                events?.Signal(DriverEvents.SessionLockLost);
                events?.Signal(DriverEvents.Tuners);
            }
        );

    private TimeSpan Interval(SessionPurpose purpose)
    {
        TimeSpan configured = configuration.Tuner?.SignalQualityInterval ?? SignalQualityReader.DefaultInterval;

        return purpose is SessionPurpose.Scan && SignalQualityReader.WhileWalkingChannels < configured
            ? SignalQualityReader.WhileWalkingChannels
            : configured;
    }

    private void Announce()
    {
        events?.Signal(DriverEvents.Sessions);
        events?.Signal(DriverEvents.Tuners);
    }

    private void RecordTuneFailure(string deviceId, TuningKey tuning, string cause)
    {
        int streak;

        lock (streakGate)
        {
            if (!tuneFailureStreaks.TryGetValue(deviceId, out Dictionary<TuningKey, int>? perChannel))
            {
                perChannel = tuneFailureStreaks[deviceId] = [];
            }

            streak = perChannel[tuning] = perChannel.TryGetValue(tuning, out int seen)
                ? seen + 1
                : 1;
        }

        if (streak < RepeatedTuneFailureCeiling)
        {
            return;
        }

        Fault(
            deviceId,
            $"The device failed to receive {tuning} {streak} times in a row without delivering"
                + $" anything in between; the last failure was: {cause}"
        );
    }

    private void ForgiveTuneFailures(string deviceId)
    {
        lock (streakGate)
        {
            tuneFailureStreaks.Remove(deviceId);
        }
    }

    public void Fault(string deviceId, string detail)
    {
        faultedDevices[deviceId] = detail;
        healthChangedAt[deviceId] = timeProvider.GetUtcNow();

        events?.Signal(DriverEvents.TunerHealthChanged);
        events?.Signal(DriverEvents.Tuners);
    }

    public bool IsClaimed(string deviceId) => pool.IsHeld(deviceId);

    public bool IsEnabled(DeviceSettings device) =>
        device.Id is { } deviceId && toggledDevices.TryGetValue(deviceId, out bool enabled)
            ? enabled
            : device.Enabled;

    public bool IsToggled(DeviceSettings device) =>
        device.Id is { } deviceId
        && toggledDevices.TryGetValue(deviceId, out bool enabled)
        && enabled != device.Enabled;

    public bool Turn(string deviceId, bool disabled)
    {
        DeviceSettings? device = (configuration.Devices ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate?.Id, deviceId, StringComparison.Ordinal)
        );

        if (device is null)
        {
            return false;
        }

        toggledDevices[deviceId] = !disabled;
        healthChangedAt[deviceId] = timeProvider.GetUtcNow();

        events?.Signal(DriverEvents.TunerHealthChanged);
        events?.Signal(DriverEvents.Tuners);

        return true;
    }

    public DateTimeOffset? HealthChangedAt(string deviceId) =>
        healthChangedAt.TryGetValue(deviceId, out DateTimeOffset changed) ? changed : null;

    public bool IsFaulted(string deviceId, [NotNullWhen(true)] out string? detail) =>
        faultedDevices.TryGetValue(deviceId, out detail);

    private static TunerKind KindOf(StartSessionRequest request) =>
        request.Tune?.Kind ?? request.Tuning.Kind;

    private static bool Matches(DeviceKind device, TunerKind requested) =>
        (device, requested) switch
        {
            (DeviceKind.Terrestrial, TunerKind.Terrestrial) => true,
            (DeviceKind.Satellite, TunerKind.Satellite) => true,
            _ => false,
        };
}
