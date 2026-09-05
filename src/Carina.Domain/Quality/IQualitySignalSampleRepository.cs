namespace Carina.Domain.Quality;

public interface IQualitySignalSampleRepository
{
    Task AddAsync(IReadOnlyList<QualitySignalSample> samples, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualitySignalSample>> ListTakenBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken);

    Task<int> ForgetTakenBeforeAsync(DateTime cutoff, CancellationToken cancellationToken);
}
