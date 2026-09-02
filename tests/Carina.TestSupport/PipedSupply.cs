using System.IO.Pipelines;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class PipedSupply : ILiveSupply
{
    private readonly Lock gate = new();

    private readonly List<PipedTransportStream> opened = [];

    private int asked;

    public int Asked => Volatile.Read(ref asked);

    public IReadOnlyList<PipedTransportStream> Opened
    {
        get
        {
            lock (gate)
            {
                return [.. opened];
            }
        }
    }

    public TaskCompletionSource? HeldUntil { get; set; }

    public LiveRefusal? Refusing { get; set; }

    public async Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);

        if (HeldUntil is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        if (Refusing is { } why)
        {
            return LiveSupplyStart.Refused(why, "held back for the test.");
        }

        PipedTransportStream stream = new(network, service);

        lock (gate)
        {
            opened.Add(stream);
        }

        return LiveSupplyStart.Opened(stream);
    }
}

public sealed class PipedTransportStream : ILiveTransportStream
{
    private readonly Pipe pipe = new();

    private bool completed;

    public PipedTransportStream(NetworkId network, ServiceId service)
    {
        Network = network;
        Service = service;
        Bytes = pipe.Reader.AsStream();
    }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public Stream Bytes { get; }

    public LiveSupplyEnding? Ending { get; set; }

    public bool Disposed { get; private set; }

    public async Task WriteAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await pipe.Writer.WriteAsync(bytes);
    }

    public void NoMore() => Complete();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Complete();

        return ValueTask.CompletedTask;
    }

    private void Complete()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        pipe.Writer.Complete();
    }
}
