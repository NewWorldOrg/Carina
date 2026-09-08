using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualitySignalSampleRepository(CarinaDbContext context) : IQualitySignalSampleRepository
{
    public async Task AddAsync(IReadOnlyList<QualitySignalSample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count is 0)
        {
            return;
        }

        context.AddRange(samples);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QualitySignalSample>> ListTakenBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => await context.Set<QualitySignalSample>()
            .AsNoTracking()
            .Where(sample => sample.TakenAt >= from && sample.TakenAt < until)
            .OrderBy(sample => sample.TakenAt)
            .ThenBy(sample => sample.DriverInstanceId)
            .ThenBy(sample => sample.Session)
            .ToListAsync(cancellationToken);

    public async Task<int> ForgetTakenBeforeAsync(DateTime cutoff, CancellationToken cancellationToken)
        => await context.Set<QualitySignalSample>()
            .Where(sample => sample.TakenAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
