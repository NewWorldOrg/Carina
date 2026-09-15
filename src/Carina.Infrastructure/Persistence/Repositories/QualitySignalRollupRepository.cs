using System.Data.Common;

using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualitySignalRollupRepository(CarinaDbContext context) : IQualitySignalRollupRepository
{
    private const string FoldedWindows = """
        SELECT date_bin(@step, window_start, @grid),
               tuner_device_id,
               network_id,
               service_id,
               sum(samples)::bigint,
               sum(locked)::bigint,
               sum(unmeasured)::bigint,
               sum(unreachable)::bigint,
               min(cnr_lowest),
               max(window_start) FILTER (WHERE cnr_lowest IS NOT NULL OR jsonb_array_length(bit_errors) > 0)
        FROM quality_signal_rollup
        WHERE granularity = @granularity AND window_start >= @from AND window_start < @until
        GROUP BY 1, 2, 3, 4
        ORDER BY 1, 2, 3, 4
        """;

    private const string FoldedLayers = """
        SELECT date_bin(@step, rollup.window_start, @grid),
               rollup.tuner_device_id,
               rollup.network_id,
               rollup.service_id,
               (layer.value ->> 'layer')::integer,
               max((layer.value ->> 'highest')::double precision)
        FROM quality_signal_rollup AS rollup
        CROSS JOIN LATERAL jsonb_array_elements(rollup.bit_errors) AS layer(value)
        WHERE rollup.granularity = @granularity AND rollup.window_start >= @from AND rollup.window_start < @until
        GROUP BY 1, 2, 3, 4, 5
        ORDER BY 1, 2, 3, 4, 5
        """;

    public async Task<IReadOnlyList<QualitySignalWindow>> ListFoldedAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        TimeSpan step,
        DateTime grid,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);

        Fold fold = new(granularity, from, until, step, grid);
        Dictionary<FoldKey, List<LayerErrorPeak>> layers = [];
        List<QualitySignalWindow> windows = [];

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await ReadAsync(FoldedLayers, fold, reader => Held(layers, Key(reader)).Add(
                new LayerErrorPeak(reader.GetInt32(4), reader.GetDouble(5))), cancellationToken);

            await ReadAsync(FoldedWindows, fold, reader =>
            {
                FoldKey key = Key(reader);

                windows.Add(new QualitySignalWindow(
                    key.Start,
                    new TunerDeviceId(key.Tuner),
                    new NetworkId(key.Network),
                    new ServiceId(key.Service),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    layers.TryGetValue(key, out List<LayerErrorPeak>? peaks) ? peaks : [],
                    [],
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTime>(9)));
            }, cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return windows;
    }
    public async Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rollups);

        if (rollups.Count is 0)
        {
            return;
        }

        foreach (QualitySignalRollup rollup in rollups)
        {
            bool held = await context.Set<QualitySignalRollup>()
                .AsNoTracking()
                .AnyAsync(
                    row => row.Granularity == rollup.Granularity
                           && row.WindowStart == rollup.WindowStart
                           && row.Tuner == rollup.Tuner
                           && row.Network == rollup.Network
                           && row.Service == rollup.Service,
                    cancellationToken);

            if (held)
            {
                context.Update(rollup);
            }
            else
            {
                context.Add(rollup);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QualitySignalRollup>> ListAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .AsNoTracking()
            .Where(rollup => rollup.Granularity == granularity
                             && rollup.WindowStart >= from
                             && rollup.WindowStart < until)
            .OrderBy(rollup => rollup.WindowStart)
            .ToListAsync(cancellationToken);

    public async Task<DateTime?> LatestWindowStartAsync(QualityWindow granularity, CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .AsNoTracking()
            .Where(rollup => rollup.Granularity == granularity)
            .OrderByDescending(rollup => rollup.WindowStart)
            .Select(rollup => (DateTime?)rollup.WindowStart)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> ForgetStartedBeforeAsync(
        QualityWindow granularity,
        DateTime cutoff,
        CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .Where(rollup => rollup.Granularity == granularity && rollup.WindowStart < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    private static List<LayerErrorPeak> Held(Dictionary<FoldKey, List<LayerErrorPeak>> layers, FoldKey key)
    {
        if (!layers.TryGetValue(key, out List<LayerErrorPeak>? held))
        {
            held = [];
            layers[key] = held;
        }

        return held;
    }

    private static FoldKey Key(DbDataReader reader)
        => new(reader.GetFieldValue<DateTime>(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));

    private static void Bind(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task ReadAsync(
        string query,
        Fold fold,
        Action<DbDataReader> each,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = query;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        Bind(command, "step", fold.Step);
        Bind(command, "grid", fold.Grid);
        Bind(command, "granularity", fold.Granularity.ToString());
        Bind(command, "from", fold.From);
        Bind(command, "until", fold.Until);

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            each(reader);
        }
    }

    private sealed record Fold(QualityWindow Granularity, DateTime From, DateTime Until, TimeSpan Step, DateTime Grid);

    private readonly record struct FoldKey(DateTime Start, string Tuner, int Network, int Service);
}
