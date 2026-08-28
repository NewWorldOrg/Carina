using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public sealed class RecordingDeletions
{
    private readonly Lock gate = new();

    private RecordingId? underway;

    public RecordingId? Underway
    {
        get
        {
            lock (gate)
            {
                return underway;
            }
        }
    }

    public IDisposable? Begin(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (gate)
        {
            if (underway is not null)
            {
                return null;
            }

            underway = id;
        }

        return new Turn(this);
    }

    private void Finish()
    {
        lock (gate)
        {
            underway = null;
        }
    }

    private sealed class Turn(RecordingDeletions deletions) : IDisposable
    {
        public void Dispose() => deletions.Finish();
    }
}
