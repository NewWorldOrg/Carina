using System.Collections.Concurrent;

using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// The latest end each running recording has already put to the driver and had an answer to. The
/// ledger only moves when the driver grants something later than the window already held, so an
/// answer of "no" and an answer of "less than you asked" both leave the recording exactly where it
/// was; without a memory of the asking, the next tick reads the same announcement, works out the
/// same end, and puts it again every <c>BetweenTicks</c> for as long as the guide keeps saying it.
/// A call that never reached the driver is not remembered: nothing was answered, and the next tick
/// is the retry.
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
