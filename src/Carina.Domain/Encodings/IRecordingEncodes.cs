using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

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

public interface IRecordingEncodes
{
    Task<bool> AnyUnderWayAsync(RecordingId recordingId, CancellationToken cancellationToken);

    Task<EncodesErased> EraseAsync(RecordingId recordingId, CancellationToken cancellationToken);
}
