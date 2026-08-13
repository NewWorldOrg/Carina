using System.Collections.Concurrent;

using Carina.Domain.Driver;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Driver;

public sealed class DriverSignalRelay(ILogger<DriverSignalRelay> logger) : IDriverSignals
{
    private readonly ConcurrentDictionary<Subscription, byte> subscriptions = [];

    public IDisposable Subscribe(Action<string> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        var subscription = new Subscription(this, listener);
        subscriptions[subscription] = 0;

        return subscription;
    }

    public void Publish(string name)
    {
        foreach (var subscription in subscriptions.Keys)
        {
            try
            {
                subscription.Listener(name);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "A driver signal listener failed on '{Signal}'.", name);
            }
        }
    }

    private void Forget(Subscription subscription) => subscriptions.TryRemove(subscription, out _);

    private sealed class Subscription(DriverSignalRelay relay, Action<string> listener) : IDisposable
    {
        public Action<string> Listener { get; } = listener;

        public void Dispose() => relay.Forget(this);
    }
}
