using System.Collections.Concurrent;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The deliveries the other end took whose rows could not be written as sent, held for as long as the process lives.
/// </summary>
public sealed class UnwrittenDeliveries
{
    private readonly ConcurrentDictionary<NotificationDeliveryId, DateTime> taken = new();

    public int Count => taken.Count;

    public void Keep(NotificationDeliveryId delivery, DateTime takenAt)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        taken[delivery] = takenAt;
    }

    public DateTime? TakenAt(NotificationDeliveryId delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return taken.TryGetValue(delivery, out DateTime at) ? at : null;
    }

    public void Drop(NotificationDeliveryId delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        taken.TryRemove(delivery, out _);
    }

    public void DropAllBut(IEnumerable<NotificationDeliveryId> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        HashSet<NotificationDeliveryId> kept = [.. held];

        foreach (NotificationDeliveryId delivery in taken.Keys.Where(delivery => !kept.Contains(delivery)))
        {
            taken.TryRemove(delivery, out _);
        }
    }
}
