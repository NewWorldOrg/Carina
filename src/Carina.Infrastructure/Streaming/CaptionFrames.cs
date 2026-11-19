using System.Buffers;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public enum CaptionFlowFault
{
    NotTheContainerItWasAskedFor = 1,

    AHeaderThatCannotBeRead = 2,

    AFrameCodeNobodyDefined = 3,

    AFrameTooBigToHold = 4,

    StoppedPartWayThroughAFrame = 5,

    APictureThatIsNotAPng = 6,
}

public static class CaptionFrames
{
    public const int Mouthful = 64 * 1024;

    public const int PictureStream = 0;

    public const ulong ShownIndefinitely = (ulong)uint.MaxValue * (LivePts.Hertz / 1000);

    public static async Task<CaptionFlowFault?> CarryAsync(
        Stream pictures,
        CaptionCanvas canvas,
        ChannelWriter<LiveFrame> into,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(into);

        try
        {
            return await DrawAsync(
                pictures,
                canvas,
                (at, picture) => into.TryWrite(picture is null ? LiveCaptions.Cleared(at) : LiveCaptions.Shown(at, picture)),
                cancellationToken);
        }
        finally
        {
            into.TryComplete();
        }
    }

    public static Task<CaptionFlowFault?> DrawAsync(
        Stream pictures,
        CaptionCanvas canvas,
        Func<LivePts, CaptionPicture?, bool> changed,
        CancellationToken cancellationToken)
        => DrawAsync(pictures, canvas, changed, static _ => { }, cancellationToken);

    public static async Task<CaptionFlowFault?> DrawAsync(
        Stream pictures,
        CaptionCanvas canvas,
        Func<LivePts, CaptionPicture?, bool> changed,
        Action<NutFrame> carried,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(carried);

        NutFrames frames = new();
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(Mouthful);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(canvas.FrameLength);
        CaptionFlowFault? fault = null;
        ReadOnlyMemory<byte>? previous = null;
        CaptionPicture? showing = null;
        LivePts? last = null;

        try
        {
            int read;

            while ((read = await pictures.ReadAsync(mouthful, cancellationToken)) > 0)
            {
                if (fault is not null)
                {
                    continue;
                }

                NutReading reading = frames.Read(mouthful.AsSpan(0, read));

                fault = ShowEvery(reading.Frames) ?? Of(reading.Fault);
            }

            return fault ?? Of(frames.Ended().Fault);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            return fault;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
            ArrayPool<byte>.Shared.Return(pixels);
        }

        CaptionFlowFault? ShowEvery(IReadOnlyList<NutFrame> arrived)
        {
            foreach (NutFrame frame in arrived)
            {
                if (frame.Stream is not PictureStream)
                {
                    carried(frame);

                    continue;
                }

                if (OffTheClock(frame.Pts, last))
                {
                    continue;
                }

                last = frame.Pts;

                if (previous is { } seen && seen.Span.SequenceEqual(frame.Data.Span))
                {
                    continue;
                }

                previous = frame.Data;

                if (!RgbaPng.TryDecode(frame.Data.Span, canvas.Size, pixels.AsSpan(0, canvas.FrameLength)))
                {
                    return CaptionFlowFault.APictureThatIsNotAPng;
                }

                showing = Shown(canvas.Drawn(pixels.AsSpan(0, canvas.FrameLength)), showing, frame.Pts, changed);
            }

            return null;
        }
    }

    private static bool OffTheClock(LivePts at, LivePts? last)
        => last is { } before && at.Value >= before.Value + ShownIndefinitely;

    private static CaptionFlowFault? Of(NutFault? fault)
        => fault switch
        {
            null => null,
            NutFault.NotTheContainerItWasAskedFor => CaptionFlowFault.NotTheContainerItWasAskedFor,
            NutFault.AHeaderThatCannotBeRead => CaptionFlowFault.AHeaderThatCannotBeRead,
            NutFault.AFrameCodeNobodyDefined => CaptionFlowFault.AFrameCodeNobodyDefined,
            NutFault.AFrameTooBigToHold => CaptionFlowFault.AFrameTooBigToHold,
            _ => CaptionFlowFault.StoppedPartWayThroughAFrame,
        };

    private static CaptionPicture? Shown(
        CaptionPicture? drawn,
        CaptionPicture? showing,
        LivePts at,
        Func<LivePts, CaptionPicture?, bool> changed)
    {
        if (drawn is null)
        {
            if (showing is not null)
            {
                changed(at, null);
            }

            return null;
        }

        if (showing is not null && Same(showing, drawn))
        {
            return showing;
        }

        changed(at, drawn);

        return drawn;
    }

    private static bool Same(CaptionPicture one, CaptionPicture other)
        => one.Left == other.Left
           && one.Top == other.Top
           && one.Width == other.Width
           && one.Height == other.Height
           && one.Png.Span.SequenceEqual(other.Png.Span);
}
