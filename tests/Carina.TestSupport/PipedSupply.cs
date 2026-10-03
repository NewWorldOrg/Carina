using System.IO.Pipelines;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class PipedSupply : ILiveSupply
{
    private readonly Lock gate = new();

    private readonly List<PipedTransportStream> opened = [];

    private int asked;

    private int givenUpOn;

    private int underWay;

    public int Asked => Volatile.Read(ref asked);

    public int GivenUpOn => Volatile.Read(ref givenUpOn);

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

    /// <summary>
    /// Set, an opening held by <see cref="HeldUntil"/> that its caller gives up on goes on holding the one
    /// tuner, and answers its caller, until this is completed, as the driver does while it is told to let
    /// go of a session it was still starting.
    /// </summary>
    public TaskCompletionSource? LettingGoOfAGivenUpOpening { get; set; }

    public LiveRefusal? Refusing { get; set; }

    public bool AsIfThereWereOneTuner { get; set; }

    public Dictionary<SessionId, long> DroppedOnTheWayIn { get; } = [];

    public bool DriverCannotBeAsked { get; set; }

    public Task<IReadOnlyDictionary<SessionId, long>> DroppedOnTheWayInAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<SessionId, long>>(
            DriverCannotBeAsked ? new Dictionary<SessionId, long>() : new Dictionary<SessionId, long>(DroppedOnTheWayIn));

    public async Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);

        if (HeldUntil is { } held)
        {
            Interlocked.Increment(ref underWay);

            try
            {
                await held.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref givenUpOn);

                if (LettingGoOfAGivenUpOpening is { } lettingGo)
                {
                    await lettingGo.Task;
                }

                throw;
            }
            finally
            {
                Interlocked.Decrement(ref underWay);
            }
        }

        if (Refusing is { } why)
        {
            return LiveSupplyStart.Refused(why, "held back for the test.");
        }

        if (AsIfThereWereOneTuner && (Opened.Any(stream => !stream.Disposed) || Volatile.Read(ref underWay) > 0))
        {
            return LiveSupplyStart.Refused(
                LiveRefusal.NoTunerFree,
                "the one tuner of the test is held by a stream that has not been let go, or by an opening still under way.");
        }

        PipedTransportStream stream = new(network, service)
        {
            Supply = SessionId.Parse($"live-{network.Value}-{service.Value}"),
        };

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

    public SessionId Supply { get; init; }

    public Stream Bytes { get; }

    public LiveSupplyEnding? Ending { get; set; }

    public List<DateTimeOffset> HeldOpenUntil { get; } = [];

    public bool RefusingToBeHeldOpen { get; set; }

    /// <summary>
    /// Set, the stream is one the driver has not let go of yet, until it is completed.
    /// </summary>
    public TaskCompletionSource? HeldFromBeingLetGo { get; set; }

    public bool Disposed => TimesLetGo > 0;

    public int TimesLetGo { get; private set; }

    public async Task WriteAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await pipe.Writer.WriteAsync(bytes);
    }

    public Task<bool> HoldOpenUntilAsync(DateTimeOffset until, CancellationToken cancellationToken)
    {
        HeldOpenUntil.Add(until);

        return Task.FromResult(!RefusingToBeHeldOpen);
    }

    public void NoMore() => Complete();

    public async ValueTask DisposeAsync()
    {
        if (HeldFromBeingLetGo is { } held)
        {
            await held.Task;
        }

        TimesLetGo++;
        Complete();
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
