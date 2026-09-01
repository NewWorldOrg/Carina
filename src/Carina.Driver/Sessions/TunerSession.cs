using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Diagnostics;
using Carina.Driver.Recording;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Sessions;

public sealed record SignalQualityWatch(
    TimeSpan Interval,
    Action<TunerSession, SignalQualitySample>? LockLost = null
);

public sealed class TunerSession : IDisposable
{
    public const int DefaultChunkSize = TsPacketReader.PacketLength * 100;
    public const long FaultReportInterval = 1000;

    private sealed class Handover(
        ITunerDevice replacement,
        TunerSession? host,
        SessionSubscription? seat
    )
    {
        private readonly SettledOnce settled = new();

        public ITunerDevice Replacement { get; } = replacement;

        public TunerSession? Host { get; } = host;

        public SessionSubscription? Seat { get; } = seat;

        public Task TakenUp => settled.Finished;

        public bool TryTakeUp() => settled.TrySettle();

        public void HasBeenTakenUp() => settled.HasFinished();

        public bool TryGiveUp() => settled.SettleUnlessAnotherAlreadyHas();
    }

    private readonly SignalQualityReader? quality;
    private readonly IRecordingWriter? recordingWriter;
    private readonly TimeProvider timeProvider;
    private readonly ILogger? logger;
    private readonly DiagnosticsStore? diagnostics;
    private readonly TsPacketReader packetReader = new();
    private readonly int chunkSize;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly ManualResetEventSlim seatIsMine;

    private ITunerDevice device;
    private SessionSubscription? seat;
    private long overflowsBefore;
    private long overflowsCarried;
    private Handover? handover;

    private Thread? loop;
    private SessionState state;
    private SessionStopReason stopReason;
    private Exception? failureCause;
    private Exception? firstFault;
    private string? takenBecause;
    private long faultCount;
    private long endsAtTicks;
    private long discardedBytes;
    private long resyncs;
    private int finished;

    public TunerSession(
        SessionId sessionId,
        SessionPurpose purpose,
        string deviceId,
        ITunerDevice device,
        DateTimeOffset startedAt,
        DateTimeOffset endsAt,
        TimeProvider timeProvider,
        IRecordingWriter? recordingWriter = null,
        int chunkSize = DefaultChunkSize,
        ILogger? logger = null,
        string? outputRoot = null,
        string? recordingId = null,
        DiagnosticsStore? diagnostics = null,
        SignalQualityWatch? watch = null,
        TuneParams? tune = null,
        TunerSession? ridesOn = null,
        SessionSubscription? seat = null,
        int demuxBufferBytes = TunerSettings.DefaultDemuxBufferBytes,
        bool takesTheSeatFromAnother = false
    )
    {
        if (endsAt <= startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                $"A session ends after it starts; got {endsAt:O} against {startedAt:O}."
            );
        }

        SessionId = sessionId;
        Purpose = purpose;
        DeviceId = deviceId;
        Tune = tune;
        OutputRoot = outputRoot;
        RecordingId = recordingId;
        RidesOn = ridesOn;
        this.seat = seat;
        StartedAt = startedAt;
        endsAtTicks = endsAt.UtcTicks;
        this.device = device;
        overflowsBefore = device.Overflows;
        this.recordingWriter = recordingWriter;
        this.timeProvider = timeProvider;
        this.chunkSize = chunkSize;
        this.logger = logger;
        this.diagnostics = diagnostics;
        state = SessionState.Requested;
        stopReason = SessionStopReason.Running;
        seatIsMine = new ManualResetEventSlim(!takesTheSeatFromAnother);
        Broadcaster = new SessionBroadcaster(
            surveyBlockLimit: SessionPurposes.ReadsEveryPacket(purpose)
                ? SessionBroadcaster.DefaultSurveyBlockLimit
                : TimeSpan.Zero,
            report: RecordFault,
            recordingBlockLimit: purpose is SessionPurpose.Recording
                ? TimeSpan.Zero
                : RecordingBackPressure.WithinTheDemuxWindow(demuxBufferBytes)
        );

        if (watch is not null && device.Quality is { } source)
        {
            quality = new SignalQualityReader(
                source,
                timeProvider,
                watch.Interval,
                sample => ReportLostLock(sample, watch.LockLost),
                RecordFault
            );
        }
    }

    public SessionId SessionId { get; }

    public SessionPurpose Purpose { get; }

    public string DeviceId { get; }

    public string? OutputRoot { get; }

    public string? RecordingId { get; }

    public TunerSession? RidesOn { get; private set; }

    public TuneParams? Tune { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset EndsAt => new(Interlocked.Read(ref endsAtTicks), TimeSpan.Zero);

    public SessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public SessionStopReason StopReason
    {
        get
        {
            lock (gate)
            {
                return stopReason;
            }
        }
    }

    public Exception? FailureCause
    {
        get
        {
            lock (gate)
            {
                return failureCause;
            }
        }
    }

    public Exception? FirstFault
    {
        get
        {
            lock (gate)
            {
                return firstFault;
            }
        }
    }

    public long FaultCount => Interlocked.Read(ref faultCount);

    public long DroppedChunks
    {
        get
        {
            lock (gate)
            {
                return seat?.DroppedChunks ?? 0;
            }
        }
    }

    public long DiscardedBytes => Interlocked.Read(ref discardedBytes);

    public long Resyncs => Interlocked.Read(ref resyncs);

    public long DeviceOverflows
    {
        get
        {
            lock (gate)
            {
                return overflowsCarried + Math.Max(0, device.Overflows - overflowsBefore);
            }
        }
    }

    public SignalQualitySample? Quality => quality?.Latest;

    public long LockLosses => quality?.LockLosses ?? 0;

    public bool Concluded => completion.Task.IsCompleted;

    public SessionBroadcaster Broadcaster { get; }

    public ContinuityCounterTracker Counters { get; } = new();

    public long BytesRecorded => recordingWriter?.BytesWritten ?? 0;

    public string? RecordingPath => recordingWriter?.Path;

    public Task Completion => completion.Task;

    public event Action<TunerSession>? Ended;

    public void Start()
    {
        lock (gate)
        {
            if (state is not SessionState.Requested)
            {
                throw new InvalidOperationException(
                    $"A session starts once; '{SessionId}' is already {state}."
                );
            }

            state = SessionState.Active;
        }

        var thread = new Thread(Run) { IsBackground = true, Name = $"session-{SessionId}" };

        try
        {
            thread.Start();
        }
        catch (Exception error)
        {
            Finish(SessionState.Failed, SessionStopReason.DeviceFailed, error);
            throw;
        }

        loop = thread;
    }

    public bool Extend(DateTimeOffset newEndsAt)
    {
        lock (gate)
        {
            if (state is not (SessionState.Requested or SessionState.Active))
            {
                return false;
            }

            if (newEndsAt.UtcTicks <= endsAtTicks)
            {
                return false;
            }

            Interlocked.Exchange(ref endsAtTicks, newEndsAt.UtcTicks);

            return true;
        }
    }

    public bool EndsNoLaterThan(DateTimeOffset limit)
    {
        lock (gate)
        {
            if (state is not (SessionState.Requested or SessionState.Active))
            {
                return false;
            }

            if (limit.UtcTicks >= endsAtTicks)
            {
                return false;
            }

            Interlocked.Exchange(ref endsAtTicks, limit.UtcTicks);

            return true;
        }
    }

    public bool ReadFromInstead(
        ITunerDevice replacement,
        TunerSession? host,
        SessionSubscription? takenSeat,
        TimeSpan within
    )
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var asked = new Handover(replacement, host, takenSeat);

        lock (gate)
        {
            if (state is not SessionState.Active || handover is not null)
            {
                return false;
            }

            handover = asked;
        }

        if (Answered(asked.TakenUp, within) || !asked.TryGiveUp())
        {
            return true;
        }

        lock (gate)
        {
            if (ReferenceEquals(handover, asked))
            {
                handover = null;
            }
        }

        return false;
    }

    private bool Answered(Task takenUp, TimeSpan within)
    {
        try
        {
            takenUp.WaitAsync(within, timeProvider).GetAwaiter().GetResult();

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void TheSeatIsYours() => seatIsMine.Set();

    public void Preempt(string because)
    {
        lock (gate)
        {
            takenBecause = because;
        }

        Stop(SessionStopReason.Preempted);
    }

    public void Stop(SessionStopReason reason = SessionStopReason.Requested)
    {
        lock (gate)
        {
            if (state is SessionState.Active)
            {
                state = SessionState.Stopping;
                stopReason = reason;
            }
        }

        stopping.Cancel();
    }

    public void WaitForEnd(TimeSpan timeout) => loop?.Join(timeout);

    public void Dispose()
    {
        Stop();

        if (loop is null)
        {
            Finish(SessionState.Stopped, SessionStopReason.Requested, null);

            return;
        }

        if (loop.Join(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        RecordFault(
            new TimeoutException(
                $"The session '{SessionId}' had not released the device '{DeviceId}' five seconds after it was asked to stop."
            )
        );
    }

    private void Run()
    {
        CancellationToken token = stopping.Token;

        try
        {
            seatIsMine.Wait(token);

            while (!token.IsCancellationRequested && timeProvider.GetUtcNow() < EndsAt)
            {
                byte[] chunk = Reading().Read(chunkSize, token);

                if (chunk.Length is 0)
                {
                    throw new EndOfStreamException(
                        $"The device '{DeviceId}' returned no bytes, so the stream is incomplete."
                    );
                }

                WriteOut(chunk);

                Measure(chunk);

                quality?.ReadIfDue();
            }

            Conclude(ReasonForEnd(token));
        }
        catch (RecordingWriteException error)
        {
            Finish(
                SessionState.Failed,
                SessionStopReason.RecordingFailed,
                error.InnerException ?? error
            );
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Conclude(ReasonForEnd(token));
        }
        catch (StreamCutException cut) when (
            cut.Reason is SessionStopReason.EndTimeReached && cut.InnerException is null
        )
        {
            Conclude(SessionStopReason.EndTimeReached);
        }
        catch (StreamCutException cut)
        {
            Finish(
                SessionState.Failed,
                cut.Reason is SessionStopReason.Running
                    ? SessionStopReason.Unspecified
                    : cut.Reason,
                cut
            );
        }
        catch (DvbDeviceException channel) when (
            channel.Failure is TuningFailure.NoLock or TuningFailure.LockedWithoutData
        )
        {
            Finish(SessionState.Failed, SessionStopReason.Unspecified, channel);
        }
        catch (Exception error)
        {
            Finish(SessionState.Failed, SessionStopReason.DeviceFailed, error);
        }
    }

    private ITunerDevice Reading()
    {
        ITunerDevice? previous = null;
        ITunerDevice current;

        lock (gate)
        {
            if (handover is { } asked)
            {
                if (asked.TryTakeUp())
                {
                    previous = device;
                    overflowsCarried += Math.Max(0, previous.Overflows - overflowsBefore);
                    overflowsBefore = asked.Replacement.Overflows;
                    device = asked.Replacement;
                    seat = asked.Seat;
                    RidesOn = asked.Host;
                    asked.HasBeenTakenUp();
                }

                handover = null;
            }

            current = device;
        }

        previous?.Dispose();

        return current;
    }

    private void WriteOutWhatTheDeviceHeldBack()
    {
        ITunerDevice current;

        lock (gate)
        {
            current = device;
        }

        byte[] tail = current.WhatIsHeldBack();
        if (tail.Length is 0)
        {
            return;
        }

        WriteOut(tail);

        foreach (TsPacket packet in packetReader.Read(tail))
        {
            Counters.Observe(packet);
        }
    }

    private void DisposeDevice()
    {
        ITunerDevice current;

        lock (gate)
        {
            current = device;
        }

        current.Dispose();
    }

    private void WriteOut(byte[] chunk)
    {
        if (recordingWriter is null)
        {
            return;
        }

        try
        {
            recordingWriter.Write(chunk);
        }
        catch (Exception error)
        {
            throw new RecordingWriteException(error);
        }
    }

    private SessionStopReason ReasonForEnd(CancellationToken token)
    {
        if (!token.IsCancellationRequested)
        {
            return SessionStopReason.EndTimeReached;
        }

        lock (gate)
        {
            return stopReason is SessionStopReason.Running
                ? SessionStopReason.Requested
                : stopReason;
        }
    }

    private void Conclude(SessionStopReason reason)
    {
        if (CutShort(reason) is { } cause)
        {
            Finish(SessionState.Failed, reason, cause);

            return;
        }

        Finish(SessionState.Stopped, reason, null);
    }

    private Exception? CutShort(SessionStopReason reason)
    {
        if (reason is SessionStopReason.DrainCapReached)
        {
            return new OperationCanceledException(
                $"The shutdown grace period ran out while '{SessionId}' was still recording."
            );
        }

        if (reason is not SessionStopReason.Preempted)
        {
            return null;
        }

        lock (gate)
        {
            return new OperationCanceledException(
                takenBecause
                    ?? $"The tuner '{DeviceId}' was taken from '{SessionId}' for something more important, so its stream ends here and is incomplete."
            );
        }
    }

    private void Measure(byte[] chunk)
    {
        try
        {
            foreach (TsPacket packet in packetReader.Read(chunk))
            {
                Counters.Observe(packet);
            }

            Interlocked.Exchange(ref discardedBytes, packetReader.DiscardedBytes);
            Interlocked.Exchange(ref resyncs, packetReader.ResyncCount);

            Broadcaster.Publish(chunk, stopping.Token);
        }
        catch (Exception error)
        {
            RecordFault(error);
        }
    }

    private void ReportLostLock(
        SignalQualitySample sample,
        Action<TunerSession, SignalQualitySample>? tell
    )
    {
        diagnostics?.Report(
            DiagnosticReason.TuningLost,
            $"The frontend serving '{SessionId}' on '{DeviceId}' is no longer locked as of {sample.LockReadAt:O}, so what this session is reading is no longer the channel it asked for.",
            DeviceId,
            SessionId
        );

        logger?.LogWarning(
            "Session {SessionId} on {DeviceId} lost the lock on its frontend and is still running.",
            SessionId.Value,
            DeviceId
        );

        tell?.Invoke(this, sample);
    }

    private void RecordFault(Exception error)
    {
        long seen = Interlocked.Increment(ref faultCount);

        lock (gate)
        {
            firstFault ??= error;
        }

        if (seen is 1)
        {
            diagnostics?.Report(
                DiagnosticReason.MeasurementFaulted,
                error.Message,
                DeviceId,
                SessionId
            );
        }

        if (seen is 1 || seen % FaultReportInterval is 0)
        {
            logger?.LogWarning(
                error,
                "Session {SessionId} on {DeviceId} has met {FaultCount} faults that did not stop it; its measurements are unreliable.",
                SessionId.Value,
                DeviceId,
                seen
            );
        }
    }

    private void Finish(SessionState outcome, SessionStopReason reason, Exception? cause)
    {
        if (Interlocked.Exchange(ref finished, 1) is 1)
        {
            return;
        }

        lock (gate)
        {
            state = SessionState.Stopping;
        }

        var causes = new List<Exception>();
        if (cause is not null)
        {
            causes.Add(cause);
        }

        Exception? writerFault = Close(() =>
        {
            WriteOutWhatTheDeviceHeldBack();
            recordingWriter?.Dispose();
        });
        if (writerFault is not null)
        {
            causes.Add(writerFault);
        }

        Exception? deviceFault = Close(DisposeDevice);
        if (deviceFault is not null)
        {
            causes.Add(deviceFault);
        }

        bool failed = causes.Count > 0;
        SessionStopReason ending = ReasonFor(reason, cause, writerFault, deviceFault);

        Exception? closeFault = Close(
            () => Broadcaster.Close(failed ? Combine(causes) : null, ending)
        );

        lock (gate)
        {
            state = failed ? SessionState.Failed : outcome;
            stopReason = ending;
            failureCause = failed ? Combine(causes) : null;
        }

        if (closeFault is not null)
        {
            RecordFault(closeFault);
        }

        ReportDiagnostic();

        RaiseEnded();

        ReportOutcome();

        completion.TrySetResult();
    }

    private void ReportDiagnostic()
    {
        if (diagnostics is null)
        {
            return;
        }

        DiagnosticReason reason = StopReason switch
        {
            SessionStopReason.RecordingFailed => DiagnosticReason.RecordingWriteFailed,
            SessionStopReason.DeviceFailed => DiagnosticReason.DeviceFaulted,
            SessionStopReason.DrainCapReached => DiagnosticReason.RecordingCutShort,
            _ => DiagnosticReason.Unspecified,
        };

        if (reason is DiagnosticReason.Unspecified)
        {
            return;
        }

        diagnostics.Report(
            reason,
            FailureCause?.Message
                ?? $"The session '{SessionId}' ended ({SessionStopReasonConverter.WireName(StopReason)}).",
            DeviceId,
            SessionId
        );
    }

    private void ReportOutcome()
    {
        if (logger is null)
        {
            return;
        }

        if (StopReason is SessionStopReason.DrainCapReached)
        {
            logger.LogError(
                FailureCause,
                "Session {SessionId} on {DeviceId} was cut short by shutdown after {BytesRecorded} bytes and is marked failed.",
                SessionId.Value,
                DeviceId,
                BytesRecorded
            );
        }
        else if (State is SessionState.Failed)
        {
            logger.LogError(
                FailureCause,
                "Session {SessionId} on {DeviceId} failed after {BytesRecorded} bytes.",
                SessionId.Value,
                DeviceId,
                BytesRecorded
            );
        }
        else
        {
            logger.LogInformation(
                "Session {SessionId} on {DeviceId} ended ({StopReason}) after {BytesRecorded} bytes.",
                SessionId.Value,
                DeviceId,
                StopReason,
                BytesRecorded
            );
        }

        if (FaultCount > 0)
        {
            logger.LogWarning(
                FirstFault,
                "Session {SessionId} met {FaultCount} faults that did not stop it.",
                SessionId.Value,
                FaultCount
            );
        }
    }

    private static SessionStopReason ReasonFor(
        SessionStopReason reason,
        Exception? cause,
        Exception? writerFault,
        Exception? deviceFault
    )
    {
        if (cause is not null)
        {
            return reason;
        }

        if (writerFault is not null)
        {
            return SessionStopReason.RecordingFailed;
        }

        return deviceFault is not null ? SessionStopReason.DeviceFailed : reason;
    }

    private void RaiseEnded()
    {
        Action<TunerSession>? handlers = Ended;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<TunerSession>)handler)(this);
            }
            catch (Exception error)
            {
                RecordFault(error);
            }
        }
    }

    private static Exception? Close(Action close)
    {
        try
        {
            close();

            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static Exception Combine(List<Exception> causes) =>
        causes.Count is 1 ? causes[0] : new AggregateException(causes);
}
