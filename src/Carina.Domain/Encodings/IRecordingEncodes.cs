using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// What taking a recording's encodes off the disk came to: how many files went, and the name of
/// each one that could not be removed.
/// </summary>
public sealed record EncodesErased
{
    public EncodesErased(int filesRemoved, IReadOnlyList<EncodeFileName> left)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(filesRemoved);
        ArgumentNullException.ThrowIfNull(left);

        FilesRemoved = filesRemoved;
        Left = [.. left];
    }

    public int FilesRemoved { get; }

    public IReadOnlyList<EncodeFileName> Left { get; }

    public bool EverythingIsGone => Left.Count is 0;
}

/// <summary>
/// What the encode ledger holds of one recording: whether a job of it is still waiting or running,
/// and the removal of what its jobs left on the disk — the artefacts its completed jobs made and
/// the scratch its ended jobs still owe a removal for.
/// </summary>
public interface IRecordingEncodes
{
    Task<bool> AnyUnderWayAsync(RecordingId recordingId, CancellationToken cancellationToken);

    Task<EncodesErased> EraseAsync(RecordingId recordingId, CancellationToken cancellationToken);
}
