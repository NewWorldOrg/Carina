using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentSettingsRepository(CarinaDbContext context) : ISegmentSettingsRepository
{
    public async Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == SegmentSettings.TheOnlyRow, cancellationToken);

    /// <summary>
    /// Inserts the one row, or updates whether learning is on and when it changed, in a single statement.
    /// </summary>
    public async Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_settings (id, learning, learning_changed_at)
            VALUES ({settings.Id}, {settings.Learning}, {settings.LearningChangedAt})
            ON CONFLICT (id) DO UPDATE SET
                learning = excluded.learning,
                learning_changed_at = excluded.learning_changed_at
            """,
            cancellationToken);
    }
}
