using Carina.Domain.Channels;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class OnTheFlyPlayer(
    OnTheFlySettings settings,
    LiveTranscodeSettings transcoding,
    ITranscodeBudget budget,
    IPlaybackFileStore files,
    IStreamAttributeReader attributes,
    ILiveEncoderSelector selector,
    TimeProvider clock) : IOnTheFlyPlayer
{
    public const int FirstChunk = 64 * 1024;

    public async Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        if (WhatIsStillThere(file) is not { } source)
        {
            return OnTheFlyStart.Refused(
                OnTheFlyRefusal.NothingToPlay,
                "the recording holds no bytes to transcode.");
        }

        TranscodeClaim claim = budget.Claim(TranscodePurpose.Playback);

        if (claim.Seat is not { } seat)
        {
            return OnTheFlyStart.Refused(OnTheFlyRefusal.TooManyAlready, claim.Refusal!.Said);
        }

        bool handedOver = false;

        try
        {
            OnTheFlyStart start = await StartedAsync(source, service, from, profile, seat, cancellationToken);

            handedOver = start.Running;

            return start;
        }
        finally
        {
            if (!handedOver)
            {
                seat.Dispose();
            }
        }
    }

    private async Task<OnTheFlyStart> StartedAsync(
        StreamSource source,
        ServiceId service,
        TimeSpan from,
        LiveProfile profile,
        ITranscodeSeat seat,
        CancellationToken cancellationToken)
    {
        StreamAttributeReading read = await attributes.ReadAsync(source, cancellationToken);
        LiveEncoderChoice chosen = await selector.ChooseAsync(cancellationToken);

        long began = clock.GetTimestamp();

        LiveTranscoderStart started = TranscoderProcess.Start(
            transcoding,
            [
                .. FfmpegPlaybackInvocation.Arguments(service, profile, read.Attributes, chosen.Encoder, source, from),
                .. FfmpegLiveInvocation.DeliveryFromTheStart(),
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
            new OnTheFlyBearing(began, from, profile, read.Measured, seat),
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
            bearing.Seat.Place,
            bearing.Seat.AtOnce);

        return OnTheFlyStart.Started(
            new OnTheFlyViewing(transcoder, standing, buffer.AsMemory(0, read), bearing.Seat.Dispose));
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
        => files.Find(file.Root, file.Name).Found is { HoldsAnything: true } still ? files.SourceOf(still) : null;

    private readonly record struct OnTheFlyBearing(
        long Began,
        TimeSpan From,
        LiveProfile Profile,
        bool Measured,
        ITranscodeSeat Seat);
}
