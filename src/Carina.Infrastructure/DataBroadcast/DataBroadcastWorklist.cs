using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.DataBroadcast;

public sealed class DataBroadcastWorklist(CarinaDbContext context, TimeProvider clock) : IDataBroadcastWorklist
{
    public async Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        if (atMost < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(atMost), atMost, "A pass takes the data broadcast of at least one recording.");
        }

        OutputRoot[] reachable = [.. withinReach];

        List<Row> rows = await Coming()
            .Where(recording => reachable.Contains(recording.OutputRoot))
            .OrderByDescending(recording => recording.StoppedAtActual)
            .ThenBy(recording => recording.Id)
            .Take(atMost)
            .Select(recording => new Row(recording.Id, recording.OutputRoot, recording.FileName, recording.ServiceId))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new DataBroadcastSubject(row.Id, row.OutputRoot, row.FileName, row.Service))];
    }

    public async Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        OutputRoot[] reachable = [.. withinReach];

        return await Coming()
            .Where(recording => !reachable.Contains(recording.OutputRoot))
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecordingId>> MadeAsync(CancellationToken cancellationToken)
        => await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.DataBroadcastState == DataBroadcastState.Made)
            .Select(recording => recording.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> RetryFailedAsync(CancellationToken cancellationToken)
    {
        List<Recording> due = await context.Set<Recording>()
            .Where(recording => recording.DataBroadcastState == DataBroadcastState.Failed
                                && recording.DataBroadcastAttempts < DataBroadcastProgress.TriesAtMost)
            .ToListAsync(cancellationToken);

        foreach (Recording recording in due)
        {
            recording.DataBroadcastAgain();
        }

        await context.SaveChangesAsync(cancellationToken);

        return due.Count;
    }

    public Task<bool> LostAsync(RecordingId id, CancellationToken cancellationToken)
        => MoveAsync(id, recording => recording.DataBroadcastAgain(), cancellationToken);

    public Task<bool> TakenAsync(RecordingId id, int modules, CancellationToken cancellationToken)
        => MoveAsync(id, recording => recording.DataBroadcastTaken(modules, Now()), cancellationToken);

    public Task<bool> FailedAsync(RecordingId id, CancellationToken cancellationToken)
        => MoveAsync(id, recording => recording.DataBroadcastFailed(Now()), cancellationToken);

    private async Task<bool> MoveAsync(RecordingId id, Action<Recording> move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording? recording = await context.Set<Recording>()
            .FirstOrDefaultAsync(held => held.Id == id, cancellationToken);

        if (recording is null)
        {
            return false;
        }

        move(recording);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private IQueryable<Recording> Coming()
        => context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome != null && recording.DataBroadcastState == DataBroadcastState.Coming);

    private sealed record Row(RecordingId Id, OutputRoot OutputRoot, RecordingFileName FileName, ServiceId Service);
}
