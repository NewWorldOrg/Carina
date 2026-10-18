namespace Carina.Domain.Segments;

/// <summary>
/// Where the versions of the model are kept. Nothing is removed from it.
/// </summary>
public interface ISegmentModelVersionRepository
{
    /// <summary>
    /// Every version kept, the highest number first.
    /// </summary>
    Task<IReadOnlyList<SegmentModelVersion>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="change"/> at <paramref name="at"/> as <see cref="SegmentModelVersion.Change"/>
    /// works it out, against the versions as they stand once no other change is being made, and writes what
    /// it moves all or nothing, the version leaving use first. Answers the version in use after it; none when
    /// the change names a number no version has. The only way the version in use changes.
    /// </summary>
    Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at, CancellationToken cancellationToken);

    /// <summary>
    /// The version in use; there is always exactly one.
    /// </summary>
    Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The version numbered <paramref name="number"/>, in whatever state it is; none when no version has that number.
    /// </summary>
    Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken);
}
