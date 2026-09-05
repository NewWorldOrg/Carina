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

    public static async Task<CaptionFlowFault?> CarryAsync(
        Stream pictures,
        CaptionCanvas canvas,
        ChannelWriter<LiveFrame> into,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(into);

        NutFrames frames = new();
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(Mouthful);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(canvas.FrameLength);
        CaptionFlowFault? fault = null;
        ReadOnlyMemory<byte>? previous = null;
        CaptionPicture? showing = null;

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

                foreach (NutFrame frame in reading.Frames)
                {
                    if (frame.Pts.Value >= LivePts.ComesAroundAt
                        || (previous is { } seen && seen.Span.SequenceEqual(frame.Data.Span)))
                    {
                        continue;
                    }

                    previous = frame.Data;

                    if (!RgbaPng.TryDecode(frame.Data.Span, canvas.Size, pixels.AsSpan(0, canvas.FrameLength)))
                    {
                        fault = CaptionFlowFault.APictureThatIsNotAPng;

                        break;
                    }

                    showing = Shown(canvas.Drawn(pixels.AsSpan(0, canvas.FrameLength)), showing, frame.Pts, into);
                }

                fault ??= Of(reading.Fault);
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
            into.TryComplete();
        }
    }

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
        ChannelWriter<LiveFrame> into)
    {
        if (drawn is null)
        {
            if (showing is not null)
            {
                into.TryWrite(LiveCaptions.Cleared(at));
            }

            return null;
        }

        if (showing is not null && Same(showing, drawn))
        {
            return showing;
        }

        into.TryWrite(LiveCaptions.Shown(at, drawn));

        return drawn;
    }

    private static bool Same(CaptionPicture one, CaptionPicture other)
        => one.Left == other.Left
           && one.Top == other.Top
           && one.Width == other.Width
           && one.Height == other.Height
           && one.Png.Span.SequenceEqual(other.Png.Span);
}
