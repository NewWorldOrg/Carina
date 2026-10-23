using Carina.Domain.DataBroadcast;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class DataBroadcastSettingsRepository(CarinaDbContext context) : IDataBroadcastSettingsRepository
{
    public const long WriterLock = 5_243_197_610_105;

    private static readonly string TakeTurnSql = $"SELECT pg_advisory_xact_lock({WriterLock})";

    public async Task TakeTurnAsync(CancellationToken cancellationToken)
        => await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

    public async Task<DataBroadcastSettings?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<DataBroadcastSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == DataBroadcastSettings.TheOnlyRow, cancellationToken);

    /// <summary>
    /// Inserts the one row, or writes the postal code, the prefecture and when they changed over the row held, in a
    /// single statement.
    /// </summary>
    public async Task SaveAsync(DataBroadcastSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        short? prefecture = (short?)settings.Prefecture;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO data_broadcast_settings (id, postal_code, prefecture, changed_at)
            VALUES ({settings.Id}, {settings.PostalCode}, {prefecture}, {settings.ChangedAt})
            ON CONFLICT (id) DO UPDATE SET
                postal_code = excluded.postal_code,
                prefecture = excluded.prefecture,
                changed_at = excluded.changed_at
            """,
            cancellationToken);
    }
}
