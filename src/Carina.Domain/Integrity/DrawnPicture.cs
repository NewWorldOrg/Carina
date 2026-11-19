using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record DrawnPicture
{
    public DrawnPicture(RecordingId recording, OutputRoot place, string path)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (path.StartsWith('/'))
        {
            throw new ArgumentException(
                "A path is read from the place down, so it does not start at the top of the disk.",
                nameof(path));
        }

        Recording = recording;
        Place = place;
        Path = path;
    }

    public RecordingId Recording { get; }

    public OutputRoot Place { get; }

    public string Path { get; }
}
