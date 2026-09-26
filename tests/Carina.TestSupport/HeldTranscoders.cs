using System.IO.Pipelines;
using System.Threading.Channels;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class HeldTranscoders(ITranscodeBudget budget) : ILiveTranscoderFactory
{
    public const string NoCaptionStream =
        "Stream specifier ':p:1024:s:0' in filtergraph description [0:p:1024:s:0]null[c] matches no streams.";

    private readonly Lock gate = new();

    private readonly List<HeldTranscoder> raised = [];

    public int Started
    {
        get
        {
            lock (gate)
            {
                return raised.Count;
            }
        }
    }

    public IReadOnlyList<HeldTranscoder> Raised
    {
        get
        {
            lock (gate)
            {
                return [.. raised];
            }
        }
    }

    public TaskCompletionSource? HeldUntil { get; set; }

    public TranscoderFault? Failing { get; set; }

    public bool WithoutACaptionStream { get; set; }

    public async Task<LiveTranscoderStart> StartAsync(
        ServiceId service,
        LiveProfile profile,
        SoundTrack sound,
        StreamAttributes attributes,
        CaptionOutlet captions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);

        TranscodeClaim claim = budget.Claim(TranscodePurpose.Live);

        if (claim.Seat is not { } seat)
        {
            return LiveTranscoderStart.Refused(claim.Refusal!);
        }

        if (HeldUntil is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        if (Failing is { } fault)
        {
            seat.Dispose();

            return LiveTranscoderStart.Failed(fault, "held back for the test.");
        }

        HeldTranscoder transcoder = new(service, profile, sound, attributes, captions, seat);

        if (WithoutACaptionStream && captions is CaptionOutlet.Drawn)
        {
            transcoder.RefuseForWantOfACaptionStream();
        }

        lock (gate)
        {
            raised.Add(transcoder);
        }

        return LiveTranscoderStart.Started(transcoder);
    }
}

public sealed class HeldTranscoder : ILiveTranscoder
{
    private readonly Pipe pipe = new();

    private readonly TakingIn input = new();

    private readonly Stream output;

    private readonly Channel<LiveFrame> captions = Channel.CreateUnbounded<LiveFrame>();

    private readonly TaskCompletionSource<TranscoderExit> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ITranscodeSeat seat;

    private bool completed;

    public HeldTranscoder(
        ServiceId service,
        LiveProfile profile,
        SoundTrack sound,
        StreamAttributes attributes,
        CaptionOutlet captioned,
        ITranscodeSeat seat)
    {
        Service = service;
        Profile = profile;
        Sound = sound;
        Attributes = attributes;
        Captioned = captioned;
        this.seat = seat;
        output = pipe.Reader.AsStream();

        if (captioned is CaptionOutlet.None)
        {
            captions.Writer.TryComplete();
        }
    }

    public ServiceId Service { get; }

    public LiveProfile Profile { get; }

    public SoundTrack Sound { get; }

    public StreamAttributes Attributes { get; }

    public CaptionOutlet Captioned { get; }

    public LiveEncoderChoice Encoder { get; } = LiveEncoderChoice.Asked(LiveEncoder.Software);

    public Stream Input => Disposed ? throw new ObjectDisposedException(nameof(HeldTranscoder)) : input;

    public long TakenIn => input.TakenIn;

    public Exception? FailingToTake
    {
        get => input.Failing;
        set => input.Failing = value;
    }

    /// <summary>
    /// Held here, the transcoder takes no bytes until it is let go, as one that has stopped reading
    /// its input does.
    /// </summary>
    public TaskCompletionSource? TakesNothingUntil
    {
        get => input.HeldUntil;
        set => input.HeldUntil = value;
    }

    public bool InputClosed => input.Closed;

    public Stream Output => Disposed ? throw new ObjectDisposedException(nameof(HeldTranscoder)) : output;

    public ChannelReader<LiveFrame> Captions => captions.Reader;

    public Task<TranscoderExit> Completion => exit.Task;

    public Exception? FailingToStop { get; set; }

    public bool OutputOutlivesIt { get; set; }

    public bool Disposed { get; private set; }

    public async Task WriteAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await pipe.Writer.WriteAsync(bytes);
    }

    public void Draw(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        captions.Writer.TryWrite(frame);
    }

    public void NoMoreCaptions() => captions.Writer.TryComplete();

    public void NoMore()
    {
        Complete();
        captions.Writer.TryComplete();
        exit.TrySetResult(TranscoderExit.Finished());
    }

    public void RefuseForWantOfACaptionStream()
    {
        Complete();
        captions.Writer.TryComplete();
        exit.TrySetResult(TranscoderExit.Refused(234, HeldTranscoders.NoCaptionStream));
    }

    public ValueTask DisposeAsync()
    {
        if (Disposed)
        {
            return ValueTask.CompletedTask;
        }

        Disposed = true;
        Complete();
        captions.Writer.TryComplete();
        exit.TrySetResult(TranscoderExit.CalledOff(string.Empty));
        seat.Dispose();

        return FailingToStop is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }

    private void Complete()
    {
        if (completed || OutputOutlivesIt)
        {
            return;
        }

        completed = true;
        pipe.Writer.Complete();
    }

    private sealed class TakingIn : Stream
    {
        private long takenIn;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        internal long TakenIn => Interlocked.Read(ref takenIn);

        internal Exception? Failing { get; set; }

        internal TaskCompletionSource? HeldUntil { get; set; }

        internal bool Closed { get; private set; }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Failing is { } refusing)
            {
                throw refusing;
            }

            Interlocked.Add(ref takenIn, buffer.Length);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (HeldUntil is { } held)
            {
                await held.Task.WaitAsync(cancellationToken);
            }

            Write(buffer.Span);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;

            base.Dispose(disposing);
        }
    }
}
