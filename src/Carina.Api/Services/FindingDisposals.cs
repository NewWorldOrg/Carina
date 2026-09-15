using Carina.Domain.Integrity;

namespace Carina.Api.Services;

public sealed class FindingDisposals
{
    public static readonly TimeSpan LongestByDefault = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();

    private IntegrityFindingId? underway;

    public FindingDisposals()
        : this(LongestByDefault)
    {
    }

    public FindingDisposals(TimeSpan longest)
    {
        if (longest <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longest),
                longest,
                "A disposal given no time at all could never finish, so the limit is longer than nothing.");
        }

        Longest = longest;
    }

    public TimeSpan Longest { get; }

    public IntegrityFindingId? Underway
    {
        get
        {
            lock (gate)
            {
                return underway;
            }
        }
    }

    public IDisposable? Begin(IntegrityFindingId id)
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

    private sealed class Turn(FindingDisposals disposals) : IDisposable
    {
        public void Dispose() => disposals.Finish();
    }
}
