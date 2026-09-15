namespace Carina.Domain.Encodings;

/// <summary>
/// Where in the corners a station's watermark draws its outline: the pixels that were an edge in
/// most of the pictures it was learned from. A picture carries the mark when at least
/// <see cref="SeenShare"/> of those pixels are an edge in it. It covers something and nothing
/// outside the corners, so a mark read back from the ledger is refused rather than believed when
/// either is not so.
/// </summary>
public sealed class WatermarkMask
{
    public const int PackedBytes = (WatermarkFrame.Pixels + 7) / 8;

    public const double SeenShare = 0.5;

    private readonly byte[] packed;

    private readonly int[] covered;

    private WatermarkMask(byte[] packed, int[] covered)
    {
        this.packed = packed;
        this.covered = covered;
    }

    public int Pixels => covered.Length;

    public static WatermarkMask Covering(IReadOnlyCollection<int> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        byte[] packed = new byte[PackedBytes];

        foreach (int pixel in pixels)
        {
            if (!WatermarkFrame.InACorner(pixel))
            {
                throw new ArgumentException("A watermark is looked for in the corners of the picture and nowhere else.", nameof(pixels));
            }

            packed[pixel >> 3] |= (byte)(1 << (pixel & 7));
        }

        return Unpacked(packed);
    }

    public static WatermarkMask Unpacked(ReadOnlySpan<byte> packed)
    {
        if (packed.Length != PackedBytes)
        {
            throw new ArgumentException($"A watermark is kept in {PackedBytes} bytes, and this one is {packed.Length}.", nameof(packed));
        }

        List<int> covered = [];

        for (int pixel = 0; pixel < WatermarkFrame.Pixels; pixel++)
        {
            if ((packed[pixel >> 3] & (1 << (pixel & 7))) is 0)
            {
                continue;
            }

            if (!WatermarkFrame.InACorner(pixel))
            {
                throw new ArgumentException("A watermark is looked for in the corners of the picture and nowhere else.", nameof(packed));
            }

            covered.Add(pixel);
        }

        if (covered.Count is 0)
        {
            throw new ArgumentException("A watermark covers some part of a corner.", nameof(packed));
        }

        return new WatermarkMask(packed.ToArray(), [.. covered]);
    }

    public byte[] Packed() => [.. packed];

    public bool Covers(int x, int y)
    {
        int pixel = WatermarkFrame.At(x, y);

        return (packed[pixel >> 3] & (1 << (pixel & 7))) is not 0;
    }

    public bool SeenIn(ReadOnlySpan<byte> frame)
    {
        WatermarkFrame.Sized(frame);

        int edges = 0;

        foreach (int pixel in covered)
        {
            if (WatermarkFrame.IsEdge(frame, pixel))
            {
                edges++;
            }
        }

        return edges >= covered.Length * SeenShare;
    }
}
