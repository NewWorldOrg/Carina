namespace Carina.Domain.Segments;

/// <summary>
/// Reads when what learning reads last changed.
/// </summary>
public interface ILearningMaterialChanges
{
    /// <summary>
    /// The latest moment an answer was written or withdrawn, a catalog entry or a stretch of one was kept, a skip
    /// event was taken, or the learning data of a recording was read to its end; none while none of them has
    /// happened.
    /// </summary>
    Task<DateTime?> LastChangedAsync(CancellationToken cancellationToken);
}
