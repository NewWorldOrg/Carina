using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Captions;

public sealed class CaptionWorklist(CarinaDbContext context, TimeProvider clock) : ICaptionWorklist
{
    public Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
        => NewestAsync(Waiting(), withinReach, atMost, cancellationToken);

    public Task<IReadOnlyList<CaptionSubject>> ReadyAmongAsync(
        IReadOnlyCollection<RecordingId> among,
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(among);

        RecordingId[] asked = [.. among];
        IQueryable<Recording> ready = context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.CaptionState == CaptionState.Ready && asked.Contains(recording.Id));

        return NewestAsync(ready, withinReach, atMost, cancellationToken);
    }

    public async Task<int> WaitingOutOfReachAsync(
        IReadOnlyList<OutputRoot> withinReach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        OutputRoot[] reachable = [.. withinReach];

        return await Waiting()
            .Where(recording => !reachable.Contains(recording.OutputRoot))
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecordingId>> ReadyAsync(CancellationToken cancellationToken)
        => await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.CaptionState == CaptionState.Ready)
            .Select(recording => recording.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> CaptionAsync(
        RecordingId id,
        CaptionState state,
        int? pictures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording? recording = await context.Set<Recording>()
            .FirstOrDefaultAsync(held => held.Id == id, cancellationToken);

        if (recording is null)
        {
            return false;
        }

        recording.Caption(state, pictures, clock.GetUtcNow().UtcDateTime);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static async Task<IReadOnlyList<CaptionSubject>> NewestAsync(
        IQueryable<Recording> chosen,
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        if (atMost < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(atMost), atMost, "A pass takes the captions of at least one recording.");
        }

        OutputRoot[] reachable = [.. withinReach];

        List<Row> rows = await chosen
            .Where(recording => reachable.Contains(recording.OutputRoot))
            .OrderByDescending(recording => recording.StoppedAtActual)
            .ThenBy(recording => recording.Id)
            .Take(atMost)
            .Select(recording => new Row(recording.Id, recording.OutputRoot, recording.FileName, recording.ServiceId))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new CaptionSubject(row.Id, row.OutputRoot, row.FileName, row.Service))];
    }

    private IQueryable<Recording> Waiting()
        => context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome != null
                                && (recording.CaptionState == CaptionState.Pending
                                    || (recording.CaptionState == CaptionState.Failed
                                        && recording.CaptionAttempts < CaptionSettings.TriesAtMost)
                                    || recording.DescrambledAt > recording.CaptionsMadeAt));

    private sealed record Row(RecordingId Id, OutputRoot OutputRoot, RecordingFileName FileName, ServiceId Service);
}
