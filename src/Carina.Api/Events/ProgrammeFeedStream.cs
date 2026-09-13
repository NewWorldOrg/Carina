using System.Globalization;
using System.Text;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Events;

public sealed record FeedReset(string Op);

public static class ProgrammeFeedStream
{
    public const string Path = "/api/programs/bulk";

    public const string ContentType = "application/x-ndjson";

    public const string CursorHeader = "X-Carina-Cursor";

    public static async Task Invoke(HttpContext context, ProgrammeFeedService feed, ProgrammeFeedReaders readers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(readers);

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

        if (!readers.TryTake(out IDisposable? place))
        {
            await TurnAwayAsync(context, readers);

            return;
        }

        using (place)
        {
            ServiceResult<FeedPage> read;

            try
            {
                read = await feed.ReadAsync(
                    from,
                    BulkCursor.Rows(Rows(context)),
                    context.RequestAborted);
            }
            catch (ReadTookTooLongException)
            {
                await StopShortAsync(context, readers, from);

                return;
            }

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
    }

    private static int? Rows(HttpContext context)
        => int.TryParse(context.Request.Query["rows"], out int asked) ? asked : null;

    private static async Task RefuseAsync(HttpContext context)
        => await SayAsync(
            context,
            StatusCodes.Status400BadRequest,
            "A cursor names the generation it belongs to and how far it has read, as in 1:0.");

    private static async Task TurnAwayAsync(HttpContext context, ProgrammeFeedReaders readers)
    {
        context.Response.Headers[HeaderNames.RetryAfter] = Patience(readers.ComeBackIn);

        await SayAsync(
            context,
            StatusCodes.Status429TooManyRequests,
            string.Create(
                CultureInfo.InvariantCulture,
                $"This installation carries {readers.Limit} bulk programme feed readers at a time and they are all taken."));
    }

    private static async Task StopShortAsync(
        HttpContext context,
        ProgrammeFeedReaders readers,
        BulkCursor? from)
    {
        context.Response.Headers[HeaderNames.RetryAfter] = Patience(readers.ComeBackIn);

        if (from is not null)
        {
            context.Response.Headers[CursorHeader] = from.Text;
        }

        await SayAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            "The store took longer than one bulk feed statement is given; nothing was sent, so ask again from the cursor.");
    }

    private static async Task SayAsync(HttpContext context, int status, string saying)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            BaseResponder<FeedReset>.Error(saying),
            WireJson.Options));
    }

    private static string Patience(TimeSpan comeBackIn)
        => Math.Max(1, (long)Math.Ceiling(comeBackIn.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
