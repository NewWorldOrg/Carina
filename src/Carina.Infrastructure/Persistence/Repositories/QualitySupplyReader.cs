using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualitySupplyReader(CarinaDbContext context, CollectionSettings settings) : IQualitySupplyReader
{
    public async Task<IReadOnlyList<SupplyReading>> ReadAsync(CancellationToken cancellationToken)
    {
        List<SupplyReading> read = [];

        read.AddRange(await WritingAsync(cancellationToken));
        read.AddRange(await VisitedAsync(cancellationToken));

        return read;
    }

    private async Task<IReadOnlyList<SupplyReading>> WritingAsync(CancellationToken cancellationToken)
    {
        List<Writing> held = await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome == null)
            .OrderBy(recording => recording.StartedAtActual)
            .ThenBy(recording => recording.Id)
            .Select(recording => new Writing(
                recording.Id,
                recording.StartedAtActual,
                recording.WrittenDurationMs,
                recording.MeasuredUpdatedAt))
            .ToListAsync(cancellationToken);

        List<SupplyReading> read = [];

        foreach (Writing row in held)
        {
            QualitySubject subject = QualitySubject.Of(QualitySubjectKind.Recording, row.Recording.Wire);

            read.Add(SupplyReading.Of(
                SupplySilence.RecordingProgress,
                subject,
                row.StartedAt + TimeSpan.FromMilliseconds(row.WrittenMilliseconds)));
            read.Add(SupplyReading.Of(
                SupplySilence.RecordingMeasurement,
                subject,
                row.MeasuredUpdatedAt ?? row.StartedAt));
        }

        return read;
    }

    private async Task<IReadOnlyList<SupplyReading>> VisitedAsync(CancellationToken cancellationToken)
    {
        List<StreamVisit> walked = await context.Set<StreamVisit>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        DateTime? soonestDue = null;
        DateTime? latestAttempt = null;

        foreach (StreamVisit visit in walked)
        {
            if (CollectionBackOff.NotBefore(visit, settings) is { } due && (soonestDue is null || due < soonestDue))
            {
                soonestDue = due;
            }

            if (latestAttempt is null || visit.LastAttemptedAt > latestAttempt)
            {
                latestAttempt = visit.LastAttemptedAt;
            }
        }

        if (soonestDue is not { } overdue)
        {
            return [];
        }

        DateTime heard = latestAttempt is { } attempted && attempted > overdue ? attempted : overdue;

        return [SupplyReading.Of(SupplySilence.GuideVisits, QualitySubject.TheGuideLedger, heard)];
    }

    private sealed record Writing(
        RecordingId Recording,
        DateTime StartedAt,
        long WrittenMilliseconds,
        DateTime? MeasuredUpdatedAt);
}
