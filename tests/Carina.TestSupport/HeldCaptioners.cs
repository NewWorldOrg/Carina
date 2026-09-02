using System.IO.Pipelines;
using System.Threading.Channels;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class HeldCaptioners : ILiveCaptionerFactory
{
    private readonly Lock gate = new();

    private readonly List<HeldCaptioner> raised = [];

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

    public IReadOnlyList<HeldCaptioner> Raised
    {
        get
        {
            lock (gate)
            {
                return [.. raised];
            }
        }
    }

    public TranscoderFault? Failing { get; set; }

    public Task<LiveCaptionerStart> StartAsync(ServiceId service, StreamAttributes attributes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(attributes);

        if (Failing is { } fault)
        {
            return Task.FromResult(LiveCaptionerStart.Failed(fault, "held back for the test."));
        }

        HeldCaptioner captioner = new(service, attributes);

        lock (gate)
        {
            raised.Add(captioner);
        }

        return Task.FromResult(LiveCaptionerStart.Started(captioner));
    }
}

public sealed class HeldCaptioner : ILiveCaptioner
{
    private readonly Pipe input = new();

    private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

    private readonly TaskCompletionSource<TranscoderExit> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HeldCaptioner(ServiceId service, StreamAttributes attributes)
    {
        Service = service;
        Attributes = attributes;
        Input = input.Writer.AsStream();
        Fed = input.Reader.AsStream();
    }

    public ServiceId Service { get; }

    public StreamAttributes Attributes { get; }

    public Stream Input { get; }

    public Stream Fed { get; }

    public ChannelReader<LiveFrame> Frames => frames.Reader;

    public Task<TranscoderExit> Completion => exit.Task;

    public Exception? FailingToStop { get; set; }

    public bool Disposed { get; private set; }

    public void Draw(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        frames.Writer.TryWrite(frame);
    }

    public void NoMore()
    {
        frames.Writer.TryComplete();
        exit.TrySetResult(TranscoderExit.Finished());
    }

    public ValueTask DisposeAsync()
    {
        if (Disposed)
        {
            return ValueTask.CompletedTask;
        }

        Disposed = true;
        frames.Writer.TryComplete();
        input.Writer.Complete();
        exit.TrySetResult(TranscoderExit.CalledOff(string.Empty));

        return FailingToStop is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }
}
