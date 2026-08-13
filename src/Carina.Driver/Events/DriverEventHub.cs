using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Contracts;

namespace Carina.Driver.Events;

public sealed class DriverEventListener : IDisposable
{
    private readonly Channel<byte> doorbell = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    private readonly Action<DriverEventListener> release;

    private int pending;

    internal DriverEventListener(Action<DriverEventListener> release) => this.release = release;

    internal void Offer(int bit)
    {
        Interlocked.Or(ref pending, bit);
        doorbell.Writer.TryWrite(0);
    }

    internal void Close() => doorbell.Writer.TryComplete();

    public async Task<IReadOnlyList<string>> Take(CancellationToken cancellationToken)
    {
        await doorbell.Reader.ReadAsync(cancellationToken);

        return DriverEventHub.NamesIn(Interlocked.Exchange(ref pending, 0));
    }

    public void Dispose()
    {
        release(this);
        Close();
    }
}

public sealed class DriverEventHub
{
    public const int DefaultListenerLimit = 16;

    private readonly ConcurrentDictionary<DriverEventListener, byte> listeners = [];
    private readonly int listenerLimit;
    private readonly Lock gate = new();

    private bool closed;

    public DriverEventHub(int listenerLimit = DefaultListenerLimit) =>
        this.listenerLimit = listenerLimit;

    public int ListenerCount => listeners.Count;

    public int ListenerLimit => listenerLimit;

    public void Signal(string name)
    {
        var bit = BitFor(name);

        foreach (var listener in listeners.Keys)
        {
            listener.Offer(bit);
        }
    }

    public bool TryListen([NotNullWhen(true)] out DriverEventListener? listener)
    {
        listener = new DriverEventListener(Forget);

        lock (gate)
        {
            if (closed || listeners.Count >= listenerLimit)
            {
                listener = null;

                return false;
            }

            listeners[listener] = 0;
        }

        return true;
    }

    public void CloseAll()
    {
        lock (gate)
        {
            closed = true;
        }

        foreach (var listener in listeners.Keys)
        {
            Forget(listener);
            listener.Close();
        }
    }

    private void Forget(DriverEventListener listener) => listeners.TryRemove(listener, out _);

    internal static IReadOnlyList<string> NamesIn(int mask)
    {
        if (mask is 0)
        {
            return [];
        }

        var names = new List<string>(DriverEvents.All.Count);

        for (var index = 0; index < DriverEvents.All.Count; index++)
        {
            if ((mask & (1 << index)) is not 0)
            {
                names.Add(DriverEvents.All[index]);
            }
        }

        return names;
    }

    private static int BitFor(string name)
    {
        for (var index = 0; index < DriverEvents.All.Count; index++)
        {
            if (string.Equals(DriverEvents.All[index], name, StringComparison.Ordinal))
            {
                return 1 << index;
            }
        }

        throw new ArgumentException(
            $"'{name}' is not one of the signals this driver is allowed to send; the set is fixed at {string.Join(", ", DriverEvents.All)}.",
            nameof(name)
        );
    }
}
