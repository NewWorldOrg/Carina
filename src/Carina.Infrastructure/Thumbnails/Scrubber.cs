using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public sealed class Scrubber(
    IRecordingDirectory recordings,
    IThumbnailRenderer renderer,
    IntegritySettings mounts) : IScrubFrames
{
    public async Task<ScrubFrame> AtAsync(RecordingId id, TimeSpan at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (at < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(at),
                at,
                "A picture is taken out of a recording, not before it.");
        }

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return ScrubFrame.Refused(ScrubRefusal.NoSuchRecording);
        }

        if (recording.IsInFlight)
        {
            return ScrubFrame.Refused(ScrubRefusal.StillBeingWritten);
        }

        if (Mounted(recording.OutputRoot) is not { } root)
        {
            return ScrubFrame.Refused(ScrubRefusal.SourceOutOfReach);
        }

        ThumbnailRender drawn = await renderer.FrameAsync(
            new ThumbnailFrameRequest(
                Path.Combine(root, recording.FileName.Value),
                recording.ServiceId,
                at),
            cancellationToken);

        return drawn.Picture is { } picture
            ? ScrubFrame.Of(picture)
            : ScrubFrame.Refused(
                drawn.Fault is ThumbnailFault.SourceOutOfReach
                    ? ScrubRefusal.SourceOutOfReach
                    : ScrubRefusal.NothingWasDrawn);
    }

    private string? Mounted(OutputRoot root)
        => mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root))?.Path;
}
