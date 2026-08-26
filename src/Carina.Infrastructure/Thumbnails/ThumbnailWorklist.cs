using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Thumbnails;

public sealed class ThumbnailWorklist(CarinaDbContext context) : IThumbnailWorklist
{
    public async Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        if (atMost < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(atMost), atMost, "A pass draws at least one picture.");
        }

        OutputRoot[] reachable = [.. withinReach];

        List<Row> rows = await Waiting()
            .Where(recording => reachable.Contains(recording.OutputRoot))
            .OrderBy(recording => recording.StoppedAtActual)
            .ThenBy(recording => recording.Id)
            .Take(atMost)
            .Select(recording => new Row(
                recording.Id,
                recording.OutputRoot,
                recording.FileName,
                recording.Outcome,
                recording.WrittenDurationMs))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Read)];
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

    public async Task IllustrateAsync(
        RecordingId id,
        ThumbnailState state,
        ThumbnailFault? fault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording recording = await context.Set<Recording>()
                                  .FirstOrDefaultAsync(held => held.Id == id, cancellationToken)
                              ?? throw new InvalidOperationException(
                                  $"There is no recording {id.Wire} to illustrate.");

        recording.Illustrate(state, fault);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ThumbnailSubject?> AskAgainAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording? recording = await context.Set<Recording>()
            .FirstOrDefaultAsync(held => held.Id == id, cancellationToken);

        if (recording?.Outcome is not { } outcome)
        {
            return null;
        }

        recording.Illustrate(ThumbnailState.Pending);

        await context.SaveChangesAsync(cancellationToken);

        return new ThumbnailSubject(
            recording.Id,
            recording.OutputRoot,
            recording.FileName,
            outcome,
            recording.Written);
    }

    private IQueryable<Recording> Waiting()
        => context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome != null && recording.ThumbnailState == ThumbnailState.Pending);

    private static ThumbnailSubject Read(Row row)
    {
        if (row.Outcome is not { } outcome)
        {
            throw new InvalidOperationException(
                $"Recording {row.Id.Wire} was selected as ended and carries no outcome.");
        }

        return new ThumbnailSubject(
            row.Id,
            row.OutputRoot,
            row.FileName,
            outcome,
            TimeSpan.FromMilliseconds(row.WrittenDurationMs));
    }

    private sealed record Row(
        RecordingId Id,
        OutputRoot OutputRoot,
        RecordingFileName FileName,
        RecordingOutcome? Outcome,
        long WrittenDurationMs);
}
