namespace Carina.Domain.Segments;

/// <summary>
/// Takes in what viewers did with the skip buttons and their seeks, looking up the version a skip button was offered
/// by. A skip event naming a version is taken only while that version is kept; one naming none comes from a chapter
/// an encode marked. Each is kept only beside fewer than the most an hour its recording takes
/// (<see cref="SkipEvent.IsTakenBeside"/>, <see cref="SeekEvent.IsTakenBeside"/>), which the store counts as it keeps
/// it.
/// </summary>
public sealed class ViewerActionIntakeDomainService(
    ISegmentModelVersionRepository versions,
    ISkipEventRepository skips,
    ISeekEventRepository seeks)
{
    /// <summary>
    /// Whether a skip button said to be offered by the version numbered <paramref name="modelVersion"/> could have
    /// been: it names none, or a version kept.
    /// </summary>
    public async Task<bool> IsOfferedByAVersionKeptAsync(int? modelVersion, CancellationToken cancellationToken)
        => modelVersion is not { } number || await versions.FindAsync(number, cancellationToken) is not null;

    /// <summary>
    /// Keeps <paramref name="skip"/> and says so; false when its recording has taken the most it takes in the hour.
    /// </summary>
    public async Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skip);

        return await skips.TakeAsync(skip, cancellationToken);
    }

    /// <summary>
    /// Keeps <paramref name="seek"/> and says so; false when its recording has taken the most it takes in the hour.
    /// </summary>
    public async Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seek);

        return await seeks.TakeAsync(seek, cancellationToken);
    }
}
