using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Sessions;

public sealed class TunerSession : IDisposable
{
    public const int DefaultChunkSize = TsPacketReader.PacketLength * 100;

    private readonly ITunerDevice device;
    private readonly IRecordingWriter? recordingWriter;
    private readonly TimeProvider timeProvider;
    private readonly TsPacketReader packetReader = new();
    private readonly int chunkSize;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private Thread? loop;
    private SessionState state;
    private SessionStopReason stopReason;
    private Exception? failureCause;
    private Exception? firstFault;
    private long faultCount;
    private long endsAtTicks;
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
        int chunkSize = DefaultChunkSize
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
        StartedAt = startedAt;
        endsAtTicks = endsAt.UtcTicks;
        this.device = device;
        this.recordingWriter = recordingWriter;
        this.timeProvider = timeProvider;
        this.chunkSize = chunkSize;
        state = SessionState.Requested;
        stopReason = SessionStopReason.Running;
        Broadcaster = new SessionBroadcaster(
            surveyBlockLimit: purpose is SessionPurpose.Recording
                ? TimeSpan.Zero
                : SessionBroadcaster.DefaultSurveyBlockLimit
        );
    }

    public SessionId SessionId { get; }

    public SessionPurpose Purpose { get; }

    public string DeviceId { get; }

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

        loop = new Thread(Run) { IsBackground = true, Name = $"session-{SessionId}" };

        try
        {
            loop.Start();
        }
        catch (Exception error)
        {
            Finish(SessionState.Failed, SessionStopReason.DeviceFailed, error);
            throw;
        }
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
        loop?.Join(TimeSpan.FromSeconds(5));
        Finish(SessionState.Stopped, SessionStopReason.Requested, null);
    }

    private void Run()
    {
        var token = stopping.Token;

        try
        {
            while (!token.IsCancellationRequested && timeProvider.GetUtcNow() < EndsAt)
            {
                var chunk = device.Read(chunkSize, token);

                if (chunk.Length is 0)
                {
                    throw new EndOfStreamException(
                        $"The device '{DeviceId}' returned no bytes, so the stream is incomplete."
                    );
                }

                recordingWriter?.Write(chunk);

                Measure(chunk);
            }

            Finish(SessionState.Stopped, ReasonForEnd(token), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Finish(SessionState.Stopped, ReasonForEnd(token), null);
        }
        catch (Exception error)
        {
            Finish(SessionState.Failed, SessionStopReason.DeviceFailed, error);
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

    private void Measure(byte[] chunk)
    {
        try
        {
            foreach (var packet in packetReader.Read(chunk))
            {
                Counters.Observe(packet);
            }

            Broadcaster.Publish(chunk, stopping.Token);
        }
        catch (Exception error)
        {
            RecordFault(error);
        }
    }

    private void RecordFault(Exception error)
    {
        Interlocked.Increment(ref faultCount);

        lock (gate)
        {
            firstFault ??= error;
        }
    }

    private void Finish(SessionState outcome, SessionStopReason reason, Exception? cause)
    {
        if (Interlocked.Exchange(ref finished, 1) is 1)
        {
            return;
        }

        var causes = new List<Exception>();
        if (cause is not null)
        {
            causes.Add(cause);
        }

        Close(() => recordingWriter?.Dispose(), causes);
        Close(() => Broadcaster.Close(causes.Count > 0 ? Combine(causes) : null), causes);
        Close(device.Dispose, causes);

        lock (gate)
        {
            state = causes.Count > 0 ? SessionState.Failed : outcome;
            stopReason = causes.Count > 0 ? SessionStopReason.DeviceFailed : reason;
            failureCause = causes.Count > 0 ? Combine(causes) : null;
        }

        RaiseEnded();

        completion.TrySetResult();
    }

    private void RaiseEnded()
    {
        var handlers = Ended;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
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

    private static void Close(Action close, List<Exception> causes)
    {
        try
        {
            close();
        }
        catch (Exception error)
        {
            causes.Add(error);
        }
    }

    private static Exception Combine(List<Exception> causes) =>
        causes.Count is 1 ? causes[0] : new AggregateException(causes);
}
