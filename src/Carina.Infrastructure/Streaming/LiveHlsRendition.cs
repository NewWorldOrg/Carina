using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class LiveHlsRendition(
    HlsHolding holding,
    ILiveViewing seat,
    LiveProfile profile,
    HlsSettings settings,
    TimeProvider clock,
    Action<HlsRendition> lapsed)
    : HlsRendition(holding, profile, new HlsSegmenter(settings), settings, clock, lapsed)
{
    protected override async Task ReadAsync()
    {
        ChannelReader<LiveFrame> frames = seat.Frames;

        try
        {
            while (await frames.WaitToReadAsync(Stopping))
            {
                Cut(frames);
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            lock (Gate)
            {
                Segmenter.End();
            }

            SayTheEndCame();
            await seat.DisposeAsync();
        }
    }

    private void Cut(ChannelReader<LiveFrame> frames)
    {
        lock (Gate)
        {
            while (frames.TryRead(out LiveFrame? frame))
            {
                Segmenter.Take(frame);
            }
        }

        SayWhetherASegmentIsCut();
    }
}
