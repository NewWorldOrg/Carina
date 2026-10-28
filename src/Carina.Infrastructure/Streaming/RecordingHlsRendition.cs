using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// A recording's transcoder, read as it writes and cut into the segments of an event, read no further ahead of the
/// furthest segment a player has asked for than the settings allow, so that a transcoder faster than the player waits
/// for it.
/// </summary>
internal sealed class RecordingHlsRendition(
    HlsHolding holding,
    IOnTheFlyViewing transcode,
    HlsSettings settings,
    TimeProvider clock,
    Action<HlsRendition> lapsed)
    : HlsRendition(
        holding,
        transcode.Standing.Profile,
        new HlsSegmenter(settings, HlsPlaylistType.Event),
        settings,
        clock,
        lapsed)
{
    private const int Mouthful = 64 * 1024;

    private readonly SemaphoreSlim asked = new(0);

    private long furthestAsked = -1;

    /// <summary>
    /// The segment numbered <paramref name="number"/>; asking for one further than any asked for before lets the
    /// transcoder go on, and lets go of those further behind it than the segments held.
    /// </summary>
    public override ReadOnlyMemory<byte>? Segment(long number)
    {
        ReadOnlyMemory<byte>? found;
        bool further;

        lock (Gate)
        {
            found = Segmenter.Segment(number);
            further = found is not null && number > furthestAsked;

            if (further)
            {
                furthestAsked = number;
                Segmenter.ForgetBefore(number - Settings.SegmentsHeld);
            }
        }

        if (further)
        {
            asked.Release();
        }

        return found;
    }

    protected override async Task ReadAsync()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        LiveFrames frames = new();
        byte[] mouthful = new byte[Mouthful];
        bool wroteToItsEnd = false;

        try
        {
            while (true)
            {
                int read = await transcode.Output.ReadAsync(mouthful, Stopping);

                if (read is 0)
                {
                    wroteToItsEnd = !fragmenter.Ended().Broke;

                    break;
                }

                LiveFragmenting cut = fragmenter.Read(mouthful.AsSpan(0, read));

                foreach (LiveFragment fragment in cut.Fragments)
                {
                    await RoomAsync();
                    Take(frames.Of(fragment));
                }

                if (cut.Broke)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
        }
        finally
        {
            lock (Gate)
            {
                if (wroteToItsEnd)
                {
                    Segmenter.Finish();
                }
                else
                {
                    Segmenter.End();
                }
            }

            SayTheEndCame();
            await transcode.DisposeAsync();
        }
    }

    private async Task RoomAsync()
    {
        while (Ahead() >= Settings.SegmentsAhead)
        {
            await asked.WaitAsync(Stopping);
        }
    }

    private long Ahead()
    {
        lock (Gate)
        {
            return Segmenter.Cut - 1 - furthestAsked;
        }
    }

    private void Take(LiveFrame frame)
    {
        lock (Gate)
        {
            Segmenter.Take(frame);
        }

        SayWhetherASegmentIsCut();
    }
}
