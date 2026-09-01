using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class OnTheFlyPlayer(
    OnTheFlySettings settings,
    LiveTranscodeSettings transcoding,
    IPlaybackFileStore files,
    IStreamAttributeReader attributes,
    ILiveEncoderSelector selector,
    TimeProvider clock) : IOnTheFlyPlayer
{
    public const int FirstChunk = 64 * 1024;

    private readonly Lock counting = new();

    private int running;

    public int Running
    {
        get
        {
            lock (counting)
            {
                return running;
            }
        }
    }

    public async Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        if (WhatIsStillThere(file) is not { } source)
        {
            return OnTheFlyStart.Refused(
                OnTheFlyRefusal.NothingToPlay,
                "the recording holds no bytes to transcode.");
        }

        if (Claimed() is not { } place)
        {
            return OnTheFlyStart.Refused(
                OnTheFlyRefusal.TooManyAlready,
                $"{settings.AtOnce} recording(s) are already being transcoded, which is as many at once as this machine is asked to.");
        }

        bool handedOver = false;

        try
        {
            OnTheFlyStart start = await StartedAsync(source, from, profile, place, cancellationToken);

            handedOver = start.Running;

            return start;
        }
        finally
        {
            if (!handedOver)
            {
                LetGo();
            }
        }
    }

    private async Task<OnTheFlyStart> StartedAsync(
        StreamSource source,
        TimeSpan from,
        LiveProfile profile,
        int place,
        CancellationToken cancellationToken)
    {
        StreamAttributeReading read = await attributes.ReadAsync(source, cancellationToken);
        LiveEncoderChoice chosen = await selector.ChooseAsync(cancellationToken);

        long began = clock.GetTimestamp();

        LiveTranscoderStart started = TranscoderProcess.Start(
            transcoding,
            [
                .. FfmpegPlaybackInvocation.Arguments(profile, read.Attributes, chosen.Encoder, source, from),
                .. FfmpegLiveInvocation.Delivery(),
            ],
            chosen,
            clock,
            cancellationToken);

        if (started.Transcoder is not { } transcoder)
        {
            return OnTheFlyStart.Refused(OnTheFlyRefusal.TranscoderWouldNotStart, started.Note);
        }

        return await WhatCameOutAsync(
            transcoder,
            new OnTheFlyBearing(began, from, profile, read.Measured, place),
            cancellationToken);
    }

    private async Task<OnTheFlyStart> WhatCameOutAsync(
        ILiveTranscoder transcoder,
        OnTheFlyBearing bearing,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[FirstChunk];
        Task<int> mouthful = transcoder.Output.ReadAsync(buffer, cancellationToken).AsTask();
        int read;

        try
        {
            read = await mouthful.WaitAsync(settings.LongestWaitForTheFirstByte, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            await AwayWith(transcoder, mouthful);

            return OnTheFlyStart.Refused(
                OnTheFlyRefusal.TookTooLong,
                $"nothing had come out of the transcoder after {settings.LongestWaitForTheFirstByte}.");
        }
        catch (OperationCanceledException)
        {
            await AwayWith(transcoder, mouthful);

            throw;
        }

        if (read is 0)
        {
            TranscoderExit ended = await transcoder.Completion;

            await transcoder.DisposeAsync();

            return OnTheFlyStart.Refused(OnTheFlyRefusal.NothingCameOut, WhatItSaid(ended));
        }

        var standing = new OnTheFlyStanding(
            bearing.From,
            clock.GetElapsedTime(bearing.Began),
            bearing.Profile,
            transcoder.Encoder,
            bearing.Measured,
            bearing.Place,
            settings.AtOnce);

        return OnTheFlyStart.Started(
            new OnTheFlyViewing(transcoder, standing, buffer.AsMemory(0, read), LetGo));
    }

    private static string WhatItSaid(TranscoderExit ended)
        => ended.Note.Length is 0
            ? "the transcoder ended without writing a picture."
            : ended.Note;

    private static async Task AwayWith(ILiveTranscoder transcoder, Task<int> mouthful)
    {
        await transcoder.DisposeAsync();

        try
        {
            await mouthful;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
    }

    private StreamSource? WhatIsStillThere(PlaybackFile file)
        => files.Find(file.Root, file.Name) is { HoldsAnything: true } still ? files.SourceOf(still) : null;

    private int? Claimed()
    {
        lock (counting)
        {
            if (running >= settings.AtOnce)
            {
                return null;
            }

            running++;

            return running;
        }
    }

    private void LetGo()
    {
        lock (counting)
        {
            running--;
        }
    }

    private readonly record struct OnTheFlyBearing(
        long Began,
        TimeSpan From,
        LiveProfile Profile,
        bool Measured,
        int Place);
}
