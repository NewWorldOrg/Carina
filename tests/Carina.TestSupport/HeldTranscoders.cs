using System.IO.Pipelines;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class HeldTranscoders(ITranscodeBudget budget) : ILiveTranscoderFactory
{
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

    public async Task<LiveTranscoderStart> StartAsync(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
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

        HeldTranscoder transcoder = new(service, profile, attributes, seat);

        lock (gate)
        {
            raised.Add(transcoder);
        }

        return LiveTranscoderStart.Started(transcoder);
    }
}

public sealed class HeldTranscoder : ILiveTranscoder
{
    private readonly Pipe output = new();

    private readonly TaskCompletionSource<TranscoderExit> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ITranscodeSeat seat;

    private bool completed;

    public HeldTranscoder(ServiceId service, LiveProfile profile, StreamAttributes attributes, ITranscodeSeat seat)
    {
        Service = service;
        Profile = profile;
        Attributes = attributes;
        this.seat = seat;
        Output = output.Reader.AsStream();
    }

    public ServiceId Service { get; }

    public LiveProfile Profile { get; }

    public StreamAttributes Attributes { get; }

    public LiveEncoderChoice Encoder { get; } = LiveEncoderChoice.Asked(LiveEncoder.Software);

    public Stream Input { get; } = Stream.Null;

    public Stream Output { get; }

    public Task<TranscoderExit> Completion => exit.Task;

    public bool Disposed { get; private set; }

    public async Task WriteAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await output.Writer.WriteAsync(bytes);
    }

    public void NoMore()
    {
        Complete();
        exit.TrySetResult(TranscoderExit.Finished());
    }

    public ValueTask DisposeAsync()
    {
        if (Disposed)
        {
            return ValueTask.CompletedTask;
        }

        Disposed = true;
        Complete();
        exit.TrySetResult(TranscoderExit.CalledOff(string.Empty));
        seat.Dispose();

        return ValueTask.CompletedTask;
    }

    private void Complete()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        output.Writer.Complete();
    }
}
