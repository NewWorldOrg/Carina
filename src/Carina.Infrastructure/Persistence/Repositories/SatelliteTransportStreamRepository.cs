using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SatelliteTransportStreamRepository(CarinaDbContext context)
    : ISatelliteTransportStreamRepository
{
    public async Task<IReadOnlyList<SatelliteTransportStream>> ListAsync(CancellationToken cancellationToken)
        => await Streams()
            .OrderBy(stream => stream.BsChannel)
            .ThenBy(stream => stream.RelativeStreamNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SatelliteTransportStream>> ListForSlotAsync(
        int bsChannel,
        CancellationToken cancellationToken)
        => await Streams()
            .Where(stream => stream.BsChannel == bsChannel)
            .OrderBy(stream => stream.RelativeStreamNumber)
            .ToListAsync(cancellationToken);

    public async Task ReplaceSlotAsync(
        int bsChannel,
        IReadOnlyList<SatelliteTransportStream> streams,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streams);

        if (streams.Any(stream => stream.BsChannel != bsChannel))
        {
            throw new ArgumentException(
                $"Every stream replacing slot {bsChannel} belongs to it.",
                nameof(streams));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Set<SatelliteTransportStream>()
            .Where(stream => stream.BsChannel == bsChannel)
            .ExecuteDeleteAsync(cancellationToken);

        context.AddRange(streams);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<SatelliteTransportStream> Streams()
        => context.Set<SatelliteTransportStream>().AsNoTracking();
}
