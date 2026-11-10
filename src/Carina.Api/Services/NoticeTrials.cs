using Carina.Domain.Notifications;

namespace Carina.Api.Services;

/// <summary>
/// Counts the trials asked for from a screen, so that one destination and the whole of them are tried no more often
/// than a window allows.
/// </summary>
public sealed class NoticeTrials(NotificationDeliverySettings settings, TimeProvider clock)
{
    private readonly Lock guard = new();

    private readonly Queue<DateTime> every = new();

    private readonly Dictionary<NotificationDestinationId, Queue<DateTime>> byDestination = [];

    /// <summary>
    /// Counts one trial of <paramref name="destination"/> and says whether it may be sent now.
    /// </summary>
    public bool Admit(NotificationDestinationId destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        lock (guard)
        {
            Trim(now - settings.Window);

            int ofThisOne = byDestination.TryGetValue(destination, out Queue<DateTime>? tried) ? tried.Count : 0;

            if (every.Count >= settings.MostTrialsInAWindow || ofThisOne >= settings.MostTrialsOfOneInAWindow)
            {
                return false;
            }

            every.Enqueue(now);
            byDestination.TryAdd(destination, new Queue<DateTime>());
            byDestination[destination].Enqueue(now);

            return true;
        }
    }

    private void Trim(DateTime since)
    {
        Forget(every, since);

        foreach (NotificationDestinationId destination in byDestination.Keys.ToList())
        {
            Forget(byDestination[destination], since);

            if (byDestination[destination].Count is 0)
            {
                byDestination.Remove(destination);
            }
        }
    }

    private static void Forget(Queue<DateTime> tried, DateTime since)
    {
        while (tried.TryPeek(out DateTime oldest) && oldest <= since)
        {
            tried.Dequeue();
        }
    }
}
