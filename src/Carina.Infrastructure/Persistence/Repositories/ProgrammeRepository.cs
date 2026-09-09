using System.Data;
using System.Data.Common;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using NpgsqlTypes;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ProgrammeRepository(CarinaDbContext context) : IProgrammeRepository
{
    public async Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Programme>()
            .FirstOrDefaultAsync(
                programme => programme.NetworkId == id.NetworkId
                    && programme.ServiceId == id.ServiceId
                    && programme.EventId == id.EventId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Programme>> ListAsync(
        ProgrammeWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var networkId = new NetworkId(window.NetworkId);
        var serviceId = new ServiceId(window.ServiceId);

        return await context.Set<Programme>()
            .Where(programme => programme.NetworkId == networkId && programme.ServiceId == serviceId)
            .Where(programme => programme.StartsAt < window.To)
            .Where(programme => programme.EndsAt == null || programme.EndsAt > window.From)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Programme programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        await context.AddAsync(programme, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProgrammesAbsorbed> AbsorbAsync(
        IReadOnlyList<ProgrammeBroadcast> broadcasts,
        IReadOnlyList<ProgrammeService> heardWhole,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcasts);
        ArgumentNullException.ThrowIfNull(heardWhole);

        if (broadcasts.Count == 0)
        {
            return new ProgrammesAbsorbed(0, 0);
        }

        if (broadcasts.Select(broadcast => broadcast.Id).Distinct().Count() != broadcasts.Count)
        {
            throw new ArgumentException("One visit names the same programme twice; bundle them first.", nameof(broadcasts));
        }

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = ProgrammeAbsorption.Sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.Add(new NpgsqlParameter(ProgrammeAbsorption.RowsParameter, NpgsqlDbType.Jsonb)
        {
            Value = ProgrammeAbsorption.Rows(broadcasts.Select(broadcast => Programme.Discover(broadcast, at))),
        });

        if (command.Connection!.State is not ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        ProgrammesAbsorbed absorbed;

        await using (DbDataReader reading = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reading.ReadAsync(cancellationToken);

            absorbed = new ProgrammesAbsorbed((int)reading.GetInt64(0), (int)reading.GetInt64(1));
        }

        await HeardAsync(broadcasts, heardWhole, at, cancellationToken);

        return absorbed;
    }

    public async Task<DateTime?> HeardWholeAtAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var network = new NetworkId(networkId);
        var service = new ServiceId(serviceId);

        return await context.Set<Programme>()
            .Where(programme => programme.NetworkId == network && programme.ServiceId == service)
            .MaxAsync(programme => programme.LastHeardAt, cancellationToken);
    }

    private async Task HeardAsync(
        IReadOnlyList<ProgrammeBroadcast> broadcasts,
        IReadOnlyList<ProgrammeService> heardWhole,
        DateTime at,
        CancellationToken cancellationToken)
    {
        var whole = heardWhole.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();
        ProgrammeId[] named =
        [
            .. broadcasts
                .Select(broadcast => broadcast.Id)
                .Where(id => whole.Contains((id.NetworkId.Value, id.ServiceId.Value))),
        ];

        if (named.Length == 0)
        {
            return;
        }

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = ProgrammeAbsorption.HeardSql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.Add(new NpgsqlParameter(ProgrammeAbsorption.HeardParameter, NpgsqlDbType.Jsonb)
        {
            Value = ProgrammeAbsorption.Heard(named),
        });
        command.Parameters.Add(new NpgsqlParameter(ProgrammeAbsorption.HeardAtParameter, NpgsqlDbType.TimestampTz)
        {
            Value = at,
        });

        if (command.Connection!.State is not ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.EndsAt != null && programme.EndsAt < at)
            .OrderBy(programme => programme.EndsAt)
            .Take(rows)
            .ToListAsync(cancellationToken);

    public async Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        if (programmes.Count == 0)
        {
            return 0;
        }

        context.Set<Programme>().RemoveRange(programmes);

        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Programme>> ListForServicesAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            return [];
        }

        List<NetworkId> networks = [.. services.Select(service => new NetworkId(service.NetworkId)).Distinct()];
        List<ServiceId> carried = [.. services.Select(service => new ServiceId(service.ServiceId)).Distinct()];
        List<Programme> found = await context.Set<Programme>()
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => !programme.IsShadow)
            .Where(programme => programme.StartsAt < to)
            .Where(programme => programme.EndsAt == null || programme.EndsAt > from)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
        var wanted = services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return [.. found.Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))];
    }

    public async Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.Revision > revision)
            .OrderBy(programme => programme.Revision)
            .Take(rows)
            .ToListAsync(cancellationToken);

    public async Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
        => await context.Set<Programme>().ExecuteDeleteAsync(cancellationToken);

    public async Task<DateTime?> CoveredUntilAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var network = new NetworkId(networkId);
        var service = new ServiceId(serviceId);

        return await context.Set<Programme>()
            .Where(programme => programme.NetworkId == network && programme.ServiceId == service)
            .Where(programme => !programme.IsShadow)
            .MaxAsync(programme => (DateTime?)programme.StartsAt, cancellationToken);
    }
}
