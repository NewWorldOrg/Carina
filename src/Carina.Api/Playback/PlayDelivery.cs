using System.Globalization;

using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Playback;

public static class PlayDelivery
{
    public const string Path = "/api/videos/{id}/play";

    public const string Position = "from";

    public const string Quality = "profile";

    public const string Sound = "sound";

    public const string Json = "application/json";

    public const string NoSeeking = "none";

    public const string NeverCached = "no-store, private";

    public const string TheProfilesThereAre =
        "A picture is asked for by one of the profiles this application encodes, and by nothing else.";

    public const string ThePositionsThereAre =
        "A recording is played from a whole number of seconds into it, or from its beginning.";

    public const string TheSoundsThereAre =
        "A recording is played with the main sound the broadcast carried or with its secondary sound, "
        + "and with no other.";

    public const string NothingToChooseFrom =
        "A recording handed over as it is carries the one sound it was encoded with, so there is no sound "
        + "to choose. Ask for it without naming a sound.";

    public const string TheBroadcastCarriedTheOneSound =
        "The broadcast this recording was made from carried one sound, so it has no secondary sound to play.";

    public static async Task Invoke(
        HttpContext context,
        string id,
        PlaybackService playback,
        IOnTheFlyPlayer player)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(player);

        context.Response.Headers.CacheControl = NeverCached;
        context.Response.Headers.Vary = HeaderNames.Accept;

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, RecordingIdText.Description);

            return;
        }

        if (Asked(context.Request.Query[Position]) is not { } from)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, ThePositionsThereAre);

            return;
        }

        AskedProfile profile = AskedProfile.Read(context.Request.Query[Quality]);

        if (profile.Answer is ProfileAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, TheProfilesThereAre);

            return;
        }

        AskedSound sound = AskedSound.Read(context.Request.Query[Sound]);

        if (sound.Answer is SoundAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, TheSoundsThereAre);

            return;
        }

        ServiceResult<PlaybackOffer, PlaybackFailure> offered =
            await playback.OfferAsync(recordingId, context.RequestAborted);

        if (!offered.IsSuccess)
        {
            await RefuseAsync(context, PlaybackStatus.Of(offered.ErrorType), offered.ErrorMessage!);

            return;
        }

        PlaybackPlan plan = offered.Data!.Plan;
        PlaybackFile handover = offered.Data!.Handover;

        PlaybackHeaders.Say(context.Response, plan);

        if (AsksForThePlan(context.Request))
        {
            await TellAsync(context, plan, handover, offered.Data!.Service, player);

            return;
        }

        if (!plan.Transcodes)
        {
            if (sound.Track is not SoundTrack.Main)
            {
                await RefuseAsync(context, StatusCodes.Status400BadRequest, NothingToChooseFrom);

                return;
            }

            await StraightAsync(context, handover, playback);

            return;
        }

        if (sound.Track is not SoundTrack.Main
            && await WhyTheSoundIsNotThereAsync(
                handover,
                offered.Data!.Service,
                sound.Track,
                player,
                context.RequestAborted) is { } why)
        {
            await RefuseAsync(context, why.Status, why.Said);

            return;
        }

        await TranscodedAsync(context, handover, offered.Data!.Service, from, profile.Named, sound.Track, player);
    }

    private static async Task<Refusal?> WhyTheSoundIsNotThereAsync(
        PlaybackFile handover,
        ServiceId service,
        SoundTrack asked,
        IOnTheFlyPlayer player,
        CancellationToken cancellationToken)
    {
        CarriedSounds carried = await player.SoundsAsync(handover, service, cancellationToken);

        if (!carried.Known)
        {
            return new Refusal(
                StatusCodes.Status503ServiceUnavailable,
                $"The sounds this recording carries could not be read: {carried.Note}");
        }

        return carried.Holds(asked)
            ? null
            : new Refusal(StatusCodes.Status400BadRequest, TheBroadcastCarriedTheOneSound);
    }

    private static async Task TellAsync(
        HttpContext context,
        PlaybackPlan plan,
        PlaybackFile handover,
        ServiceId service,
        IOnTheFlyPlayer player)
    {
        CarriedSounds carried = plan.Transcodes
            ? await player.SoundsAsync(handover, service, context.RequestAborted)
            : CarriedSounds.Counted(0);

        context.Response.StatusCode = StatusCodes.Status200OK;

        await context.Response.WriteAsJsonAsync(
            BaseResponder<PlaybackPlanResponder>.Success(
                PlaybackPlanResponder.Of(plan, handover, MediaTypeOf(plan, handover), carried.Tracks)),
            context.RequestAborted);
    }

    private static async Task StraightAsync(HttpContext context, PlaybackFile file, PlaybackService playback)
    {
        context.Response.Headers.AcceptRanges = ByteRange.Unit;
        PlaybackHeaders.SayItStartsAtTheBeginning(context.Response);

        ByteRange asked = ByteRange.Read(context.Request.Headers.Range, file.Bytes);

        if (asked.Answer is RangeAnswer.OutOfReach)
        {
            RangedFile.Refuse(context, file.Bytes);

            return;
        }

        ServiceResult<Stream, PlaybackFailure> opened = playback.Open(file);

        if (!opened.IsSuccess)
        {
            await RefuseAsync(context, PlaybackStatus.Of(opened.ErrorType), opened.ErrorMessage!);

            return;
        }

        await using Stream reading = opened.Data!;

        RangedFile.Describe(context, PlaybackMediaType.Of(file.Name), asked, file.Bytes);
        reading.Seek(asked.From, SeekOrigin.Begin);

        await RangedFile.HandOverAsync(reading, context.Response.Body, asked.Count, context.RequestAborted);
    }

    private static async Task TranscodedAsync(
        HttpContext context,
        PlaybackFile handover,
        ServiceId service,
        TimeSpan from,
        LiveProfile? profile,
        SoundTrack sound,
        IOnTheFlyPlayer player)
    {
        context.Response.Headers.AcceptRanges = NoSeeking;

        OnTheFlyStart start = await player.StartAsync(
            handover,
            service,
            from,
            profile,
            sound,
            context.RequestAborted);

        if (start.Viewing is not { } viewing)
        {
            PlaybackHeaders.SayWhyNot(context.Response, start.Refusal!.Value);

            await RefuseAsync(context, Of(start.Refusal!.Value), Said(start.Refusal!.Value));

            return;
        }

        await using (viewing)
        {
            PlaybackHeaders.Say(context.Response, viewing.Standing);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = PlaybackMediaType.Mp4;

            try
            {
                await viewing.Output.CopyToAsync(
                    context.Response.Body,
                    RangedFile.ChunkSize,
                    context.RequestAborted);
            }
            catch (Exception gone) when (gone is OperationCanceledException or IOException)
            {
                return;
            }
        }
    }

    private static bool AsksForThePlan(HttpRequest request)
        => request.Headers.Accept.Any(offered =>
            offered?.Contains(Json, StringComparison.OrdinalIgnoreCase) is true);

    private static string MediaTypeOf(PlaybackPlan plan, PlaybackFile handover)
        => plan.Transcodes ? PlaybackMediaType.Mp4 : PlaybackMediaType.Of(handover.Name);

    private static TimeSpan? Asked(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return TimeSpan.Zero;
        }

        if (!double.TryParse(position, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return null;
        }

        return double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static Task RefuseAsync(HttpContext context, int status, string said)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            BaseResponder<PlaybackPlanResponder>.Error(said),
            context.RequestAborted);
    }

    private static int Of(OnTheFlyRefusal refusal) => refusal switch
    {
        OnTheFlyRefusal.NothingToPlay => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status503ServiceUnavailable,
    };

    private static string Said(OnTheFlyRefusal refusal) => refusal switch
    {
        OnTheFlyRefusal.NothingToPlay => "The recording holds no bytes to play.",
        OnTheFlyRefusal.TooManyAlready =>
            "As many recordings are being transcoded at once as this machine is asked to. Try again shortly.",
        OnTheFlyRefusal.TranscoderWouldNotStart => "The transcoder this recording needs would not start.",
        OnTheFlyRefusal.NothingCameOut => "The transcoder ended without producing a picture of this recording.",
        _ => "The transcoder produced nothing in the time it is given to start.",
    };

    private readonly record struct Refusal(int Status, string Said);
}
