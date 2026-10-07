namespace Carina.Domain.Segments;

public interface ISegmentSettingsRepository
{
    Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Whether CM, OP and ED learning is on, read afresh on every ask. Off where no row is held.
/// </summary>
public interface ILearningSwitch
{
    Task<bool> IsOnAsync(CancellationToken cancellationToken);
}
