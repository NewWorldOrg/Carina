using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualityLedgerReader(CarinaDbContext context, IBroadcastStreamDirectory streams) : IQualityLedgerReader
{
    public async Task<IReadOnlyList<QualityLedgerRow>> ReadAsync(
        QualityPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        List<Held> held = await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.StartedAtActual >= period.From && recording.StartedAtActual < period.Until)
            .OrderBy(recording => recording.StartedAtActual)
            .ThenBy(recording => recording.Id)
            .Select(recording => new Held(
                recording.Id,
                recording.NetworkId,
                recording.ServiceId,
                recording.TunerDeviceId,
                recording.StartedAtActual,
                recording.CcMeasured,
                recording.CcDroppedPackets,
                recording.CcTotalPackets,
                recording.ScrambledPackets,
                recording.EovfCount,
                recording.MeasuredUpdatedAt))
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<(int Network, int Service), TuneSystem> placed =
            await PlacedAsync(cancellationToken);

        return
        [
            .. held.Select(row => QualityLedgerRow.Of(
                row.Recording,
                row.Network,
                row.Service,
                placed.TryGetValue((row.Network.Value, row.Service.Value), out TuneSystem system) ? system : null,
                row.Tuner,
                row.StartedAt,
                DropCounters.Rehydrate(row.Measured, row.Dropped, row.Total),
                row.ScrambledPackets,
                row.Overflows,
                row.MeasuredUpdatedAt)),
        ];
    }

    private async Task<IReadOnlyDictionary<(int Network, int Service), TuneSystem>> PlacedAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BroadcastStream> carried = await streams.ListAsync(cancellationToken);
        Dictionary<(int Network, int Service), TuneSystem> placed = [];

        foreach (BroadcastStream stream in carried)
        {
            foreach (ServiceId service in stream.Services)
            {
                placed[(stream.NetworkId.Value, service.Value)] = stream.Tuning.System;
            }
        }

        return placed;
    }

    private sealed record Held(
        RecordingId Recording,
        NetworkId Network,
        ServiceId Service,
        TunerDeviceId? Tuner,
        DateTime StartedAt,
        bool Measured,
        long? Dropped,
        long? Total,
        long? ScrambledPackets,
        long Overflows,
        DateTime? MeasuredUpdatedAt);
}
