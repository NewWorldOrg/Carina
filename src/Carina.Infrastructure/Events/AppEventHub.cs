using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Contracts;
using Carina.Domain.Events;

namespace Carina.Infrastructure.Events;

public sealed class AppEventListener : IDisposable
{
    private readonly Channel<byte> doorbell = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly Action<AppEventListener> release;

    private int pending;

    internal AppEventListener(Action<AppEventListener> release) => this.release = release;

    public async Task<IReadOnlyList<AppEventName>> Take(CancellationToken cancellationToken)
    {
        await doorbell.Reader.ReadAsync(cancellationToken);

        return AppEventHub.NamesIn(Interlocked.Exchange(ref pending, 0));
    }

    public void Dispose()
    {
        release(this);
        Close();
    }

    internal void Offer(int bit)
    {
        Interlocked.Or(ref pending, bit);
        doorbell.Writer.TryWrite(0);
    }

    internal void Close() => doorbell.Writer.TryComplete();
}

public sealed class AppEventHub : IAppEventPublisher
{
    public const int DefaultListenerLimit = 16;

    private readonly ConcurrentDictionary<AppEventListener, byte> listeners = [];
    private readonly Lock gate = new();

    private bool closed;

    public AppEventHub(int listenerLimit = DefaultListenerLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(listenerLimit);

        ListenerLimit = listenerLimit;
    }

    public int ListenerLimit { get; }

    public int ListenerCount => listeners.Count;

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    public void Signal(AppEventName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var bit = BitFor(name);

        foreach (var listener in listeners.Keys)
        {
            listener.Offer(bit);
        }
    }

    public bool TryListen([NotNullWhen(true)] out AppEventListener? listener)
    {
        listener = new AppEventListener(Forget);

        lock (gate)
        {
            if (closed || listeners.Count >= ListenerLimit)
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

    internal static IReadOnlyList<AppEventName> NamesIn(int mask)
    {
        if (mask is 0)
        {
            return [];
        }

        var names = new List<AppEventName>(AppEventName.All.Count);

        for (var index = 0; index < AppEventName.All.Count; index++)
        {
            if ((mask & (1 << index)) is not 0)
            {
                names.Add(AppEventName.All[index]);
            }
        }

        return names;
    }

    private static int BitFor(AppEventName name)
    {
        for (var index = 0; index < AppEventName.All.Count; index++)
        {
            if (ReferenceEquals(AppEventName.All[index], name))
            {
                return 1 << index;
            }
        }

        throw new ArgumentException(
            $"'{name}' is not one of the nine app signals; the set is fixed at {string.Join(", ", AppEventName.All)}.",
            nameof(name));
    }

    private void Forget(AppEventListener listener) => listeners.TryRemove(listener, out _);
}
