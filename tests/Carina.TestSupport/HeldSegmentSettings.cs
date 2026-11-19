using Carina.Domain.Segments;

namespace Carina.TestSupport;

public sealed class HeldSegmentSettings : ISegmentSettingsRepository
{
    private SegmentSettings? held;

    public int Saves { get; private set; }

    public int Reads { get; private set; }

    public int Turns { get; private set; }

    public Task TakeTurnAsync(CancellationToken cancellationToken)
    {
        Turns++;

        return Task.CompletedTask;
    }

    public Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(held);
    }

    public Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken)
    {
        held = settings;
        Saves++;

        return Task.CompletedTask;
    }
}
