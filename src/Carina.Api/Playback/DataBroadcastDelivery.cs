using System.Globalization;

using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Playback;

/// <summary>
/// The data broadcast of a recording: its catalog over the source it is played from, each moment at the second of
/// that source, and each module version as the live side channel carries it. Both are opened with the reader's own
/// session and never with a ticket, because no player outside the browser shows them.
/// </summary>
public static class DataBroadcastDelivery
{
    public const string Path = "/api/videos/{id}/data-broadcast";

    public const string ModulePath = "/api/videos/{id}/data-broadcast/modules/{tag}/{download}/{module}/{version}";

    public const string Position = "from";

    public const string Source = "source";

    public const string Decodes = "decodes";

    public const string MediaType = "application/octet-stream";

    public const string Revalidated = "private, no-cache";

    public const string ThePositionsThereAre =
        "The data broadcast is asked for from a whole or fractional number of seconds into the source, or from its beginning.";

    public static async Task Invoke(HttpContext context, string id, PlaybackService playback, DataBroadcastService broadcast)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(broadcast);

        context.Response.Headers.CacheControl = PlayDelivery.NeverCached;

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, RecordingIdText.Description);

            return;
        }

        if (CaptionDelivery.From(context.Request.Query[Position]) is not { } from)
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

        ServiceResult<DataBroadcastTimeline, DataBroadcastFailure> timeline =
            await broadcast.TimelineAsync(recordingId, offered.Data!, from, context.RequestAborted);

        if (!timeline.IsSuccess)
        {
            await RefuseAsync(context, StatusOf(timeline.ErrorType), timeline.ErrorMessage!);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;

        await context.Response.WriteAsJsonAsync(
            BaseResponder<DataBroadcastTimelineResponder>.Success(DataBroadcastTimelineResponder.Of(timeline.Data!)),
            context.RequestAborted);
    }

    public static async Task InvokeModule(
        HttpContext context,
        string id,
        string tag,
        string download,
        string module,
        string version,
        DataBroadcastService broadcast)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(broadcast);

        context.Response.Headers.CacheControl = PlayDelivery.NeverCached;

        if (RecordingIdText.Read(id) is not { } recordingId || Key(tag, download, module, version) is not { } key)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        IList<EntityTagHeaderValue> held = context.Request.GetTypedHeaders().IfNoneMatch;
        ServiceResult<KeptModule, DataBroadcastFailure> found = await broadcast.ModuleAsync(
            recordingId,
            key,
            tag => held.Any(asked => asked.Equals(EntityTagHeaderValue.Any) || asked.Compare(new EntityTagHeaderValue(tag), useStrongComparison: false)),
            context.RequestAborted);

        if (!found.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        context.Response.Headers.CacheControl = Revalidated;
        context.Response.Headers.ETag = found.Data!.ETag;

        if (found.Data.Version is not { } answered)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;

            return;
        }

        byte[] payload = DataBroadcastFrames.ModulePayload(answered);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaType;
        context.Response.ContentLength = payload.Length;

        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    private static ModuleVersionKey? Key(string tag, string download, string module, string version)
        => byte.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out byte carousel)
           && uint.TryParse(download, NumberStyles.None, CultureInfo.InvariantCulture, out uint downloadId)
           && ushort.TryParse(module, NumberStyles.None, CultureInfo.InvariantCulture, out ushort moduleId)
           && byte.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out byte numbered)
            ? new ModuleVersionKey(carousel, downloadId, moduleId, numbered)
            : null;

    private static int StatusOf(DataBroadcastFailure failure) => failure switch
    {
        DataBroadcastFailure.Coming => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status404NotFound,
    };

    private static Task RefuseAsync(HttpContext context, int status, string said)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            BaseResponder<DataBroadcastTimelineResponder>.Error(said),
            context.RequestAborted);
    }
}
