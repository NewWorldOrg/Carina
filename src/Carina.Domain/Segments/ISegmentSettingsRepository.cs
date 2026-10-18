namespace Carina.Domain.Segments;

public interface ISegmentSettingsRepository
{
    /// <summary>
    /// Waits, inside the write it is part of, until no other change of the settings is being made, so that
    /// what is read next is what the change is made against.
    /// </summary>
    Task TakeTurnAsync(CancellationToken cancellationToken);

    Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the one row as it is handed over, in place of the row held where there is one.
    /// </summary>
    Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Whether CM, OP and ED learning is on, read afresh on every ask. Off where no row is held.
/// </summary>
public interface ILearningSwitch
{
    Task<bool> IsOnAsync(CancellationToken cancellationToken);
}
