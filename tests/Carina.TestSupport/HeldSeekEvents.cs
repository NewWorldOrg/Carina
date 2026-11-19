using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.TestSupport;

public sealed class HeldSeekEvents : ISeekEventRepository
{
    private readonly Lock gate = new();

    private readonly List<SeekEvent> taken = [];

    public IReadOnlyList<SeekEvent> Taken
    {
        get
        {
            lock (gate)
            {
                return [.. taken];
            }
        }
    }

    public Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seek);

        lock (gate)
        {
            if (!seek.IsTakenBeside(taken.Where(kept => kept.RecordingId.Equals(seek.RecordingId)).Select(kept => kept.HappenedAt)))
            {
                return Task.FromResult(false);
            }

            taken.Add(SeekEvent.Rehydrate(taken.Count + 1, seek.RecordingId, seek.HappenedAt, seek.Event, seek.Means, seek.From, seek.To));

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<SeekEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SeekEvent>>([.. Taken.Where(seek => recordings.Contains(seek.RecordingId)).OrderBy(seek => seek.HappenedAt).ThenBy(seek => seek.Id)]);
}
