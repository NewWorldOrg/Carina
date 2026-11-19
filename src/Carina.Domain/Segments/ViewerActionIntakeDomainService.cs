namespace Carina.Domain.Segments;

public sealed class ViewerActionIntakeDomainService(
    ISegmentModelVersionRepository versions,
    ISkipEventRepository skips,
    ISeekEventRepository seeks)
{
    public async Task<bool> IsOfferedByAVersionKeptAsync(int? modelVersion, CancellationToken cancellationToken)
        => modelVersion is not { } number || await versions.FindAsync(number, cancellationToken) is not null;

    public async Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skip);

        return await skips.TakeAsync(skip, cancellationToken);
    }

    public async Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seek);

        return await seeks.TakeAsync(seek, cancellationToken);
    }
}
