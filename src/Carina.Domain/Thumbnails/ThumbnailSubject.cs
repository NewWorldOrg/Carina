using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public sealed record ThumbnailSubject
{
    public ThumbnailSubject(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        RecordingOutcome outcome,
        TimeSpan written)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A recording ends in one of three ways.");
        }

        if (written < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(written),
                written,
                "A recording is not shorter than nothing.");
        }

        Id = id;
        Root = root;
        FileName = fileName;
        Outcome = outcome;
        Written = written;
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public RecordingOutcome Outcome { get; }

    public TimeSpan Written { get; }
}
