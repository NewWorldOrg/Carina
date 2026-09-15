using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class StationWatermarkRepository(CarinaDbContext context) : IStationWatermarkRepository
{
    public async Task<StationWatermark?> FindAheadOfAsync(
        NetworkId networkId,
        ServiceId serviceId,
        RecordingId judged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(judged);

        return await context.Set<StationWatermark>()
            .AsNoTracking()
            .Where(watermark => watermark.NetworkId == networkId
                && watermark.ServiceId == serviceId
                && watermark.LearnedFrom != judged)
            .OrderByDescending(watermark => watermark.LearnedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task KeepAsync(StationWatermark learned, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(learned);

        NetworkId networkId = learned.NetworkId;
        ServiceId serviceId = learned.ServiceId;
        RecordingId learnedFrom = learned.LearnedFrom;

        await context.Set<StationWatermark>()
            .Where(watermark => watermark.NetworkId == networkId
                && watermark.ServiceId == serviceId
                && watermark.LearnedFrom == learnedFrom)
            .ExecuteDeleteAsync(cancellationToken);

        context.Add(learned);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.Entry(learned).State = EntityState.Detached;
        }

        DateTime? oldestKept = await context.Set<StationWatermark>()
            .Where(watermark => watermark.NetworkId == networkId && watermark.ServiceId == serviceId)
            .OrderByDescending(watermark => watermark.LearnedAt)
            .Skip(StationWatermark.KeptPerService - 1)
            .Select(watermark => (DateTime?)watermark.LearnedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (oldestKept is not { } cutoff)
        {
            return;
        }

        await context.Set<StationWatermark>()
            .Where(watermark => watermark.NetworkId == networkId
                && watermark.ServiceId == serviceId
                && watermark.LearnedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
