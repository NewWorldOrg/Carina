using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public sealed class DrawnThumbnails(
    IRecordingDirectory recordings,
    ThumbnailSettings settings) : IDrawnThumbnails
{
    public async Task<DrawnThumbnail> OfAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return DrawnThumbnail.Refused(DrawnThumbnailRefusal.NoSuchRecording, ThumbnailState.Pending);
        }

        if (recording.ThumbnailState is not ThumbnailState.Ready)
        {
            return DrawnThumbnail.Refused(DrawnThumbnailRefusal.NoPictureWasDrawn, recording.ThumbnailState);
        }

        if (!settings.DrawsAnything)
        {
            return DrawnThumbnail.Refused(DrawnThumbnailRefusal.PictureOutOfReach, recording.ThumbnailState);
        }

        try
        {
            byte[] read = await File.ReadAllBytesAsync(
                System.IO.Path.Combine(settings.WrittenTo!, id.Wire + ThumbnailJob.Extension),
                cancellationToken);

            return read.Length > 0
                ? DrawnThumbnail.Of(read)
                : DrawnThumbnail.Refused(DrawnThumbnailRefusal.PictureOutOfReach, recording.ThumbnailState);
        }
        catch (Exception gone) when (gone is IOException or UnauthorizedAccessException)
        {
            return DrawnThumbnail.Refused(DrawnThumbnailRefusal.PictureOutOfReach, recording.ThumbnailState);
        }
    }
}
