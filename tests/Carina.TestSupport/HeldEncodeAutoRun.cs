using Carina.Domain.Encodings;

namespace Carina.TestSupport;

public sealed class HeldEncodeAutoRun : IEncodeAutoRunRepository
{
    private EncodeAutoRun? held;

    public int Saves { get; private set; }

    public Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(held);

    public Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken)
    {
        held = autoRun;
        Saves++;

        return Task.CompletedTask;
    }
}

public sealed class StandingEncodeAutoRun(EncodeAutoRunStanding standing) : IEncodeAutoRunReader
{
    public EncodeAutoRunStanding Standing { get; set; } = standing;

    public StandingEncodeAutoRun()
        : this(new EncodeAutoRunStanding(true, 2, false, null))
    {
    }

    public Task<EncodeAutoRunStanding> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Standing);
}
