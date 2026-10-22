using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentSettingsRepository(CarinaDbContext context) : ISegmentSettingsRepository
{
    public const long WriterLock = 5_243_197_610_104;

    private static readonly string TakeTurnSql = $"SELECT pg_advisory_xact_lock({WriterLock})";

    public async Task TakeTurnAsync(CancellationToken cancellationToken)
        => await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

    public async Task<SegmentSettings?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == SegmentSettings.TheOnlyRow, cancellationToken);

    /// <summary>
    /// Inserts the one row, or writes the threshold and when it changed over the row held, in a single statement.
    /// </summary>
    public async Task SaveAsync(SegmentSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        decimal threshold = (decimal)settings.SkipThreshold;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_settings (id, skip_threshold, skip_threshold_changed_at)
            VALUES ({settings.Id}, {threshold}, {settings.SkipThresholdChangedAt})
            ON CONFLICT (id) DO UPDATE SET
                skip_threshold = excluded.skip_threshold,
                skip_threshold_changed_at = excluded.skip_threshold_changed_at
            """,
            cancellationToken);
    }
}
