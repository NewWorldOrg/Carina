using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class RecordingHlsRendition(
    HlsHolding holding,
    IOnTheFlyViewing transcode,
    RecordingId recording,
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

    internal RecordingId Recording { get; } = recording;

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
        HlsEnding ending = HlsEnding.CalledOff;

        try
        {
            ending = await CutToTheEndAsync();
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            ending = HlsEnding.Broke;
        }
        finally
        {
            Settle(ending);
            SayTheEndCame();
            await transcode.DisposeAsync();
        }
    }

    private async Task<HlsEnding> CutToTheEndAsync()
    {
        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        LiveFrames frames = new();
        byte[] mouthful = new byte[Mouthful];

        while (true)
        {
            int read = await transcode.Output.ReadAsync(mouthful, Stopping);

            if (read is 0)
            {
                bool whole = !fragmenter.Ended().Broke && (await transcode.Completion.WaitAsync(Stopping)).RanToTheEnd;

                return whole ? HlsEnding.Whole : HlsEnding.Broke;
            }

            LiveFragmenting cut = fragmenter.Read(mouthful.AsSpan(0, read));

            foreach (LiveFragment fragment in cut.Fragments)
            {
                await RoomAsync();
                Take(frames.Of(fragment));
            }

            if (cut.Broke)
            {
                return HlsEnding.Broke;
            }
        }
    }

    private void Settle(HlsEnding ending)
    {
        if (ending is HlsEnding.Broke)
        {
            SayItBroke();

            return;
        }

        lock (Gate)
        {
            if (ending is HlsEnding.Whole)
            {
                Segmenter.Finish();
            }
            else
            {
                Segmenter.End();
            }
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

internal enum HlsEnding
{
    CalledOff = 1,

    Whole = 2,

    Broke = 3,
}
