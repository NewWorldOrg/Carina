using System.Buffers;
using System.Globalization;

using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Playback;

public static class VideoDelivery
{
    public const string Path = "/api/videos/{id}";

    public const int ChunkSize = 64 * 1024;

    public static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];

    public static async Task Invoke(HttpContext context, string id, PlaybackService playback)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);

        context.Response.Headers.AcceptRanges = ByteRange.Unit;

        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

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
            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            context.Response.Headers.ContentRange = Beyond(file.Bytes);

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

        await HandOverAsync(reading, context.Response.Body, asked.Count, context.RequestAborted);
    }

    private static void Describe(HttpContext context, PlaybackFile file, ByteRange asked)
    {
        context.Response.ContentType = PlaybackMediaType.Of(file.Name);
        context.Response.ContentLength = asked.Count;

        if (asked.Answer is not RangeAnswer.Part)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;

            return;
        }

        context.Response.StatusCode = StatusCodes.Status206PartialContent;
        context.Response.Headers.ContentRange = Part(asked, file.Bytes);
    }

    private static string Beyond(long size)
        => string.Create(CultureInfo.InvariantCulture, $"{ByteRange.Unit} */{size}");

    private static string Part(ByteRange asked, long size)
        => string.Create(CultureInfo.InvariantCulture, $"{ByteRange.Unit} {asked.From}-{asked.Last}/{size}");

    private static async Task HandOverAsync(Stream reading, Stream writing, long count, CancellationToken stopping)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);

        try
        {
            long left = count;

            while (left > 0)
            {
                int wanted = (int)Math.Min(left, buffer.Length);
                int read = await reading.ReadAsync(buffer.AsMemory(0, wanted), stopping);

                if (read is 0)
                {
                    return;
                }

                await writing.WriteAsync(buffer.AsMemory(0, read), stopping);
                left -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
