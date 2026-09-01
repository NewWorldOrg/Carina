using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

public sealed record PlaybackFile
{
    public PlaybackFile(OutputRoot root, RecordingFileName name, long bytes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        Root = root;
        Name = name;
        Bytes = bytes;
    }

    public OutputRoot Root { get; }

    public RecordingFileName Name { get; }

    public long Bytes { get; }

    public bool HoldsAnything => Bytes > 0;
}
