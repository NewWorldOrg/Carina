using System.Buffers;
using System.Globalization;

namespace Carina.Api.Playback;

public static class RangedFile
{
    public const int ChunkSize = 64 * 1024;

    public static void Refuse(HttpContext context, long size)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
        context.Response.Headers.ContentRange =
            string.Create(CultureInfo.InvariantCulture, $"{ByteRange.Unit} */{size}");
    }

    public static void Describe(HttpContext context, string mediaType, ByteRange asked, long size)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asked);

        context.Response.ContentType = mediaType;
        context.Response.ContentLength = asked.Count;

        if (asked.Answer is not RangeAnswer.Part)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;

            return;
        }

        context.Response.StatusCode = StatusCodes.Status206PartialContent;
        context.Response.Headers.ContentRange = string.Create(
            CultureInfo.InvariantCulture,
            $"{ByteRange.Unit} {asked.From}-{asked.Last}/{size}");
    }

    public static async Task HandOverAsync(
        Stream reading,
        Stream writing,
        long count,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(writing);

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
