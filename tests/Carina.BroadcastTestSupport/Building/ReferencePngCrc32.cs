namespace Carina.BroadcastTestSupport;

public static class ReferencePngCrc32
{
    private const uint Polynomial = 0xEDB8_8320;

    private const uint Seed = 0xFFFF_FFFF;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint register = Seed;

        foreach (byte octet in data)
        {
            register ^= octet;

            for (int bit = 0; bit < 8; bit++)
            {
                register = (register & 1) != 0 ? (register >> 1) ^ Polynomial : register >> 1;
            }
        }

        return register ^ Seed;
    }
}
