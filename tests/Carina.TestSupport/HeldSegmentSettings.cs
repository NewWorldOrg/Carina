using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The one row of the segment settings, held in memory. Absent until something switches learning.
/// </summary>
public sealed class HeldSegmentSettings : ISegmentSettingsRepository
{
    private SegmentSettings? held;

    public int Saves { get; private set; }

    public int Reads { get; private set; }

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
