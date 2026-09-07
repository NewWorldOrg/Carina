using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public sealed class RecordingDeletions
{
    public static readonly TimeSpan LongestByDefault = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();

    private RecordingId? underway;

    public RecordingDeletions()
        : this(LongestByDefault)
    {
    }

    public RecordingDeletions(TimeSpan longest)
    {
        if (longest <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longest),
                longest,
                "A deletion given no time at all could never finish, so the limit is longer than nothing.");
        }

        Longest = longest;
    }

    public TimeSpan Longest { get; }

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
