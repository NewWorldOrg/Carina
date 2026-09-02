using System.Buffers;
using System.Threading.Channels;

namespace Carina.Infrastructure.Streaming;

public sealed class CaptionSupply
{
    public const int LongestBacklog = 64;

    private readonly Stream into;

    private readonly Channel<Mouthful> mouthfuls;

    private readonly Task carrying;

    private long dropped;

    public CaptionSupply(Stream into, int longestBacklog = LongestBacklog)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestBacklog, 1);

        this.into = into;
        mouthfuls = Channel.CreateBounded<Mouthful>(new BoundedChannelOptions(longestBacklog)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        carrying = CarryAsync();
    }

    public long Dropped => Volatile.Read(ref dropped);

    public bool Broken { get; private set; }

    public Task Carried => carrying;

    public void Offer(ReadOnlySpan<byte> bytes)
    {
        if (Broken || bytes.IsEmpty)
        {
            return;
        }

        byte[] held = ArrayPool<byte>.Shared.Rent(bytes.Length);

        bytes.CopyTo(held);

        if (!mouthfuls.Writer.TryWrite(new Mouthful(held, bytes.Length)))
        {
            ArrayPool<byte>.Shared.Return(held);
            Interlocked.Increment(ref dropped);
        }
    }

    public async Task CompleteAsync()
    {
        mouthfuls.Writer.TryComplete();

        await carrying;
    }

    private async Task CarryAsync()
    {
        try
        {
            while (await mouthfuls.Reader.WaitToReadAsync())
            {
                while (mouthfuls.Reader.TryRead(out Mouthful mouthful))
                {
                    try
                    {
                        if (!Broken)
                        {
                            await into.WriteAsync(mouthful.Bytes.AsMemory(0, mouthful.Length));
                            await into.FlushAsync();
                        }
                    }
                    catch (Exception gone) when (gone is IOException or ObjectDisposedException)
                    {
                        Broken = true;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(mouthful.Bytes);
                    }
                }
            }
        }
        finally
        {
            Broken = true;

            try
            {
                into.Close();
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private readonly record struct Mouthful(byte[] Bytes, int Length);
}
