using System.Globalization;

using Carina.Api.Common;
using Carina.Domain.Thumbnails;

namespace Carina.Api.Playback;

public static class ScrubDelivery
{
    public const string Path = "/api/videos/{id}/scrub";

    public const string MediaType = "image/jpeg";

    public const string Position = "at";

    public static async Task Invoke(HttpContext context, string id, IScrubFrames frames)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frames);

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        if (Asked(context.Request.Query[Position]) is not { } at)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        ScrubFrame frame = await frames.AtAsync(recordingId, at, context.RequestAborted);

        if (frame.Picture is not { } picture)
        {
            context.Response.StatusCode = Of(frame.Refusal!.Value);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaType;
        context.Response.ContentLength = picture.Length;

        await context.Response.Body.WriteAsync(picture, context.RequestAborted);
    }

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

    private static int Of(ScrubRefusal refusal) => refusal switch
    {
        ScrubRefusal.NoSuchRecording => StatusCodes.Status404NotFound,
        ScrubRefusal.StillBeingWritten => StatusCodes.Status409Conflict,
        ScrubRefusal.SourceOutOfReach => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status404NotFound,
    };
}
