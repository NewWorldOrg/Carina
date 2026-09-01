using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public enum ScrubRefusal
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    SourceOutOfReach = 3,

    NothingWasDrawn = 4,
}

public sealed record ScrubFrame
{
    private ScrubFrame(byte[]? picture, ScrubRefusal? refusal)
    {
        Picture = picture;
        Refusal = refusal;
    }

    public byte[]? Picture { get; }

    public ScrubRefusal? Refusal { get; }

    public static ScrubFrame Of(byte[] picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return picture.Length > 0
            ? new ScrubFrame(picture, null)
            : throw new ArgumentException("A picture that weighs nothing was not drawn.", nameof(picture));
    }

    public static ScrubFrame Refused(ScrubRefusal refusal)
        => Enum.IsDefined(refusal)
            ? new ScrubFrame(null, refusal)
            : throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A frame is refused for one of the reasons there are.");
}

public interface IScrubFrames
{
    Task<ScrubFrame> AtAsync(RecordingId id, TimeSpan at, CancellationToken cancellationToken);
}
