using System.Collections.Concurrent;

using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// The latest end each running recording has already put to the driver and had an answer to, so
/// that the same end is not asked for again. A call that never reached the driver is not
/// remembered.
/// </summary>
public sealed class EndsAlreadyAsked
{
    private readonly ConcurrentDictionary<RecordingId, DateTime> asked = new();

    public bool AlreadyPut(RecordingId recording, DateTime endsAt)
        => asked.TryGetValue(recording, out DateTime last) && endsAt <= last;

    public void Answered(RecordingId recording, DateTime endsAt)
        => asked.AddOrUpdate(recording, endsAt, (_, last) => endsAt > last ? endsAt : last);

    public void KeepOnly(IReadOnlySet<RecordingId> running)
    {
        ArgumentNullException.ThrowIfNull(running);

        foreach (RecordingId held in asked.Keys)
        {
            if (!running.Contains(held))
            {
                asked.TryRemove(held, out _);
            }
        }
    }
}
