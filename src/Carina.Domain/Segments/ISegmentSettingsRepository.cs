namespace Carina.Domain.Segments;

public interface ISegmentSettingsRepository
{
    Task TakeTurnAsync(CancellationToken cancellationToken);

    Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken);
}
