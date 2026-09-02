using System.Buffers;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class LiveFeed
{
    public const int Mouthful = 64 * 1024;

    public static async Task<LiveFragmentFault?> CarryAsync(
        Stream from,
        LiveFanout into,
        CancellationToken cancellationToken,
        Action<LiveFrame>? published = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(into);

        LiveFragmenter fragmenter = new(LiveTrack.Picture);
        LiveFrames frames = new();
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(Mouthful);

        try
        {
            int read;

            while ((read = await ReadAsync(from, mouthful, cancellationToken)) > 0)
            {
                LiveFragmenting made = fragmenter.Read(mouthful.AsSpan(0, read));

                foreach (LiveFragment fragment in made.Fragments)
                {
                    LiveFrame frame = frames.Of(fragment);

                    into.Publish(frame);
                    published?.Invoke(frame);
                }

                if (made.Fault is { } broke)
                {
                    into.Break(broke);

                    return broke;
                }
            }

            if (fragmenter.Ended().Fault is { } stopped)
            {
                into.Break(stopped);

                return stopped;
            }

            into.End();

            return null;
        }
        catch (OperationCanceledException)
        {
            into.End();

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
        }
    }

    private static async ValueTask<int> ReadAsync(Stream from, byte[] mouthful, CancellationToken cancellationToken)
    {
        try
        {
            return await from.ReadAsync(mouthful, cancellationToken);
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
