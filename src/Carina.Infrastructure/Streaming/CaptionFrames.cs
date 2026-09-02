using System.Buffers;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public enum CaptionFlowFault
{
    StoppedPartWayThroughAPicture = 1,

    NoStampForAPicture = 2,

    AStampForAnotherPicture = 3,
}

public static class CaptionFrames
{
    public static async Task<CaptionFlowFault?> CarryAsync(
        Stream pictures,
        TextReader said,
        CaptionCanvas canvas,
        LiveCaptionSettings settings,
        ChannelWriter<LiveFrame> into,
        CancellationToken cancellationToken,
        Action<string>? complained = null)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(into);

        Channel<CaptionStamp> stamps = Channel.CreateUnbounded<CaptionStamp>();
        Task stamping = StampAsync(said, stamps.Writer, complained, cancellationToken);
        byte[] picture = ArrayPool<byte>.Shared.Rent(canvas.FrameLength);
        byte[] previous = ArrayPool<byte>.Shared.Rent(canvas.FrameLength);
        bool anyPrevious = false;

        try
        {
            CaptionPicture? showing = null;

            for (int index = 0; ; index++)
            {
                int read = await pictures.ReadAtLeastAsync(picture.AsMemory(0, canvas.FrameLength), canvas.FrameLength, false, cancellationToken);

                if (read is 0)
                {
                    return null;
                }

                if (read < canvas.FrameLength)
                {
                    return CaptionFlowFault.StoppedPartWayThroughAPicture;
                }

                if (await StampOfAsync(stamps.Reader, index, cancellationToken) is not { } stamp)
                {
                    return CaptionFlowFault.NoStampForAPicture;
                }

                if (stamp.Index != index)
                {
                    return CaptionFlowFault.AStampForAnotherPicture;
                }

                Span<byte> current = picture.AsSpan(0, canvas.FrameLength);

                if (stamp.Pts is not { } at || (anyPrevious && current.SequenceEqual(previous.AsSpan(0, canvas.FrameLength))))
                {
                    continue;
                }

                current.CopyTo(previous);
                anyPrevious = true;
                showing = Shown(canvas.Drawn(current), showing, settings.Corrected(at), into);
            }
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(picture);
            ArrayPool<byte>.Shared.Return(previous);
            into.TryComplete();
            await Quietly(stamping);
        }
    }

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

    private static async ValueTask<CaptionStamp?> StampOfAsync(
        ChannelReader<CaptionStamp> stamps,
        int index,
        CancellationToken cancellationToken)
    {
        while (await stamps.WaitToReadAsync(cancellationToken))
        {
            while (stamps.TryRead(out CaptionStamp? stamp))
            {
                if (stamp.Index >= index)
                {
                    return stamp;
                }
            }
        }

        return null;
    }

    private static async Task StampAsync(
        TextReader said,
        ChannelWriter<CaptionStamp> into,
        Action<string>? complained,
        CancellationToken cancellationToken)
    {
        CaptionStamps reading = new();

        try
        {
            while (await said.ReadLineAsync(cancellationToken) is { } line)
            {
                if (reading.Read(line) is { } stamp)
                {
                    into.TryWrite(stamp);
                }
                else if (!line.StartsWith("[Parsed_showinfo_", StringComparison.Ordinal))
                {
                    complained?.Invoke(line);
                }
            }
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            into.TryComplete();
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
