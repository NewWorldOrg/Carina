using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Playback;

public static class VideoDelivery
{
    public const string Path = "/api/videos/{id}";

    public static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];

    public static Task Invoke(HttpContext context, string id, PlaybackService playback)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);

        context.Response.Headers.AcceptRanges = ByteRange.Unit;

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return Task.CompletedTask;
        }

        if (context.User.Identity?.IsAuthenticated is true)
        {
            return ServeAsync(context, recordingId, playback);
        }

        return context.RequestServices
            .GetRequiredService<PlaybackTicketGate>()
            .AdmitForAsLongAsTheGrantLastsAsync(
            context,
            PlaybackTicketService.TargetOf(recordingId),
            (_, _) => ServeAsync(context, recordingId, playback));
    }

    private static async Task ServeAsync(HttpContext context, RecordingId recordingId, PlaybackService playback)
    {
        ServiceResult<PlaybackOffer, PlaybackFailure> offered =
            await playback.OfferAsync(recordingId, context.RequestAborted);

        if (!offered.IsSuccess)
        {
            context.Response.StatusCode = PlaybackStatus.Of(offered.ErrorType);

            return;
        }

        PlaybackFile file = offered.Data!.Handover;
        ByteRange asked = ByteRange.Read(context.Request.Headers.Range, file.Bytes);

        if (asked.Answer is RangeAnswer.OutOfReach)
        {
            RangedFile.Refuse(context, file.Bytes);

            return;
        }

        if (HttpMethods.IsHead(context.Request.Method))
        {
            Describe(context, file, asked);

            return;
        }

        ServiceResult<Stream> opened = playback.Open(file);

        if (!opened.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return;
        }

        await using Stream reading = opened.Data!;

        Describe(context, file, asked);
        reading.Seek(asked.From, SeekOrigin.Begin);

        await RangedFile.HandOverAsync(reading, context.Response.Body, asked.Count, context.RequestAborted);
    }

    private static void Describe(HttpContext context, PlaybackFile file, ByteRange asked)
        => RangedFile.Describe(context, PlaybackMediaType.Of(file.Name), asked, file.Bytes);
}
