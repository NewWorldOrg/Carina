namespace Carina.BroadcastTestSupport;

public sealed class BitWriter
{
    private readonly List<byte> bytes = [];

    private int held;

    private int heldBits;

    public BitWriter Bits(int value, int count)
    {
        for (int bit = count - 1; bit >= 0; bit--)
        {
            held = (held << 1) | ((value >> bit) & 1);
            heldBits++;

            if (heldBits is 8)
            {
                bytes.Add((byte)held);
                held = 0;
                heldBits = 0;
            }
        }

        return this;
    }

    public BitWriter Align()
    {
        if (heldBits > 0)
        {
            Bits(0, 8 - heldBits);
        }

        return this;
    }

    public byte[] ToArray()
    {
        Align();

        return [.. bytes];
    }
}
