namespace Carina.Domain.Segments;

public interface ISegmentModelVersionRepository
{
    Task<IReadOnlyList<SegmentModelVersion>> ListAsync(CancellationToken cancellationToken);

    Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at, CancellationToken cancellationToken);

    Task<SegmentModelVersion> AddCandidateAsync(SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt, CancellationToken cancellationToken);

    Task<IReadOnlySet<int>> NamedAmongAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> LetGoAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken);

    Task<SegmentBuiltInKept?> KeepBuiltInAsync(SegmentModel builtIn, DateTime at, CancellationToken cancellationToken);

    Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken);

    Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken);
}
