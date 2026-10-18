using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The one row of the segment settings, held in memory. Absent until something changes a setting.
/// </summary>
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
