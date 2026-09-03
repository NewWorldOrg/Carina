namespace Carina.BroadcastTestSupport;

public static class ReferenceCrc16
{
    private const ushort Polynomial = 0x1021;
    private const ushort Seed = 0xFFFF;

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort register = Seed;

        foreach (byte octet in data)
        {
            register ^= (ushort)(octet << 8);

            for (int bit = 0; bit < 8; bit++)
            {
                register = (register & 0x8000) != 0
                    ? (ushort)((register << 1) ^ Polynomial)
                    : (ushort)(register << 1);
            }
        }

        return register;
    }
}
