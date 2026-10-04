using System.Globalization;

using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Captions;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

/// <summary>
/// The captions of a recording for ten minutes of the source it is played from, each at the second of that
/// source. It is opened with the reader's own session and never with a ticket, because no player outside
/// the browser draws them.
/// </summary>
public static class CaptionDelivery
{
    public const string Path = "/api/videos/{id}/captions";

    public const string Position = "from";

    public const string Source = "source";

    public const string Decodes = "decodes";

    public const string ThePositionsThereAre =
        "Captions are asked for from a whole or fractional number of seconds into the source, or from its beginning.";

    public static async Task Invoke(HttpContext context, string id, PlaybackService playback, CaptionService captions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(captions);

        context.Response.Headers.CacheControl = PlayDelivery.NeverCached;

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, RecordingIdText.Description);

            return;
        }

        if (From(context.Request.Query[Position]) is not { } from)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, ThePositionsThereAre);

            return;
        }

        AskedSource source = AskedSource.Read(context.Request.Query[Source]);

        if (source.Answer is SourceAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.TheSourcesThereAre);

            return;
        }

        AskedDecoding decoding = AskedDecoding.Read(context.Request.Query[Decodes]);

        if (decoding.Answer is DecodingAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.TheDecodingsThereAre);

            return;
        }

        ServiceResult<PlaybackOffer, PlaybackFailure> offered = await playback.OfferAsync(
            recordingId,
            SoundTrack.Main,
            source.Source,
            decoding.Audience,
            context.RequestAborted);

        if (!offered.IsSuccess)
        {
            await RefuseAsync(context, PlaybackStatus.Of(offered.ErrorType), offered.ErrorMessage!);

            return;
        }

        ServiceResult<CaptionWindow, CaptionFailure> window =
            await captions.WindowAsync(recordingId, offered.Data!, from, context.RequestAborted);

        if (!window.IsSuccess)
        {
            await RefuseAsync(context, StatusOf(window.ErrorType), window.ErrorMessage!);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;

        await context.Response.WriteAsJsonAsync(
            BaseResponder<CaptionWindowResponder>.Success(CaptionWindowResponder.Of(window.Data!)),
            context.RequestAborted);
    }

    private static TimeSpan? From(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return TimeSpan.Zero;
        }

        return double.TryParse(asked, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
               && double.IsFinite(seconds)
               && seconds >= 0
               && seconds <= TimeSpan.MaxValue.TotalSeconds - CaptionWindow.Covers.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static int StatusOf(CaptionFailure failure) => failure switch
    {
        CaptionFailure.Coming => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status404NotFound,
    };

    private static Task RefuseAsync(HttpContext context, int status, string said)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            BaseResponder<CaptionWindowResponder>.Error(said),
            context.RequestAborted);
    }
}
