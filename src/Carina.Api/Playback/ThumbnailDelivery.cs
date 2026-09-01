using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Thumbnails;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Playback;

public static class ThumbnailDelivery
{
    public const string Path = "/api/videos/{id}/thumbnail";

    public const string MediaType = "image/jpeg";

    public const string HeldBriefly = "private, max-age=60";

    public const string State = "Carina-Thumbnail-State";

    public static async Task Invoke(HttpContext context, string id, IDrawnThumbnails drawn)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(drawn);

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        DrawnThumbnail found = await drawn.OfAsync(recordingId, context.RequestAborted);

        context.Response.Headers[State] = JsonNamingPolicy.CamelCase.ConvertName(found.State.ToString());

        if (found.Picture is not { } picture)
        {
            context.Response.StatusCode = Of(found.Refusal!.Value);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaType;
        context.Response.ContentLength = picture.Length;
        context.Response.Headers[HeaderNames.CacheControl] = HeldBriefly;

        await context.Response.Body.WriteAsync(picture, context.RequestAborted);
    }

    private static int Of(DrawnThumbnailRefusal refusal) => refusal switch
    {
        DrawnThumbnailRefusal.PictureOutOfReach => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status404NotFound,
    };
}
