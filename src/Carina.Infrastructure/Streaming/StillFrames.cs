using System.Buffers;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class StillFrames
{
    public const int PictureStream = 0;

    public static async Task CarryAsync(Stream pictures, Action<LiveStill> made, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(made);

        NutFrames frames = new();
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(CaptionFrames.Mouthful);
        bool answered = false;

        try
        {
            int read;

            while ((read = await pictures.ReadAsync(mouthful, cancellationToken)) > 0)
            {
                if (answered || Of(frames.Read(mouthful.AsSpan(0, read))) is not { } still)
                {
                    continue;
                }

                answered = true;
                made(still);
            }

            if (!answered)
            {
                made(Ended(frames.Ended().Fault));
            }
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            if (!answered)
            {
                made(LiveStill.Missing("the pipe the still was written to broke."));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
        }
    }

    private static LiveStill? Of(NutReading reading)
    {
        if (reading.Fault is { } fault)
        {
            return Unreadable(fault);
        }

        if (reading.Frames.FirstOrDefault(frame => frame.Stream is PictureStream) is not { } first)
        {
            return null;
        }

        return LiveStill.IsJpeg(first.Data.Span)
            ? LiveStill.Of(first.Pts, first.Data)
            : LiveStill.Missing("the picture written for the still is not a JPEG.");
    }

    private static LiveStill Ended(NutFault? fault)
        => fault is { } stopped ? Unreadable(stopped) : LiveStill.Missing("the transcoder ended without writing a still.");

    private static LiveStill Unreadable(NutFault fault) => LiveStill.Missing($"the still could not be read: {fault}.");
}
