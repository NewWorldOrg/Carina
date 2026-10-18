namespace Carina.Domain.Segments;

/// <summary>
/// Where the versions of the model are kept. Nothing is removed from it.
/// </summary>
public interface ISegmentModelVersionRepository
{
    /// <summary>
    /// The version in use; there is always exactly one.
    /// </summary>
    Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The version numbered <paramref name="number"/>, in whatever state it is; none when no version has that number.
    /// </summary>
    Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken);
}
