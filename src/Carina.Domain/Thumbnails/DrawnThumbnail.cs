using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public enum DrawnThumbnailRefusal
{
    NoSuchRecording = 1,

    NoPictureWasDrawn = 2,

    PictureOutOfReach = 3,
}

public sealed record DrawnThumbnail
{
    private DrawnThumbnail(byte[]? picture, ThumbnailState state, DrawnThumbnailRefusal? refusal)
    {
        Picture = picture;
        State = state;
        Refusal = refusal;
    }

    public byte[]? Picture { get; }

    public ThumbnailState State { get; }

    public DrawnThumbnailRefusal? Refusal { get; }

    public static DrawnThumbnail Of(byte[] picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return picture.Length > 0
            ? new DrawnThumbnail(picture, ThumbnailState.Ready, null)
            : throw new ArgumentException("A picture that weighs nothing was not drawn.", nameof(picture));
    }

    public static DrawnThumbnail Refused(DrawnThumbnailRefusal refusal, ThumbnailState state)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A picture is withheld for one of the reasons there are.");
        }

        return new DrawnThumbnail(null, state, refusal);
    }
}

public interface IDrawnThumbnails
{
    Task<DrawnThumbnail> OfAsync(RecordingId id, CancellationToken cancellationToken);
}
