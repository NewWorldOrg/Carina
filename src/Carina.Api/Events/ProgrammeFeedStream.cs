using System.Text;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Programmes;

namespace Carina.Api.Events;

public sealed record FeedReset(string Op);

public static class ProgrammeFeedStream
{
    public const string Path = "/api/programs/bulk";

    public const string ContentType = "application/x-ndjson";

    public const string CursorHeader = "X-Carina-Cursor";

    public static async Task Invoke(HttpContext context, ProgrammeFeedService feed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(feed);

        string? asked = context.Request.Query["cursor"];
        BulkCursor? from = null;

        if (!string.IsNullOrEmpty(asked))
        {
            from = BulkCursor.Read(asked);

            if (from is null)
            {
                await RefuseAsync(context);

                return;
            }
        }

        ServiceResult<FeedPage> read = await feed.ReadAsync(
            from,
            BulkCursor.Rows(Rows(context)),
            context.RequestAborted);
        FeedPage page = read.Data!;

        context.Response.ContentType = ContentType;
        context.Response.Headers[CursorHeader] = page.Next.Text;

        await using var writing = new StreamWriter(context.Response.Body, new UTF8Encoding(false));

        if (page.StartOver)
        {
            await writing.WriteLineAsync(JsonSerializer.Serialize(new FeedReset("reset"), WireJson.Options));

            return;
        }

        foreach (Programme programme in page.Programmes)
        {
            await writing.WriteLineAsync(
                JsonSerializer.Serialize(ProgrammeResponder.Of(programme), WireJson.Options));
        }
    }

    private static int? Rows(HttpContext context)
        => int.TryParse(context.Request.Query["rows"], out int asked) ? asked : null;

    private static async Task RefuseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            BaseResponder<FeedReset>.Error(
                "A cursor names the generation it belongs to and how far it has read, as in 1:0."),
            WireJson.Options));
    }
}
