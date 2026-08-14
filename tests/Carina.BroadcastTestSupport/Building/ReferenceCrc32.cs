namespace Carina.BroadcastTestSupport;

public static class ReferenceCrc32
{
    private const uint Polynomial = 0x04C1_1DB7;
    private const uint Seed = 0xFFFF_FFFF;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var register = Seed;

        foreach (var octet in data)
        {
            register ^= (uint)octet << 24;

            for (var bit = 0; bit < 8; bit++)
            {
                register = (register & 0x8000_0000) != 0
                    ? (register << 1) ^ Polynomial
                    : register << 1;
            }
        }

        return register;
    }
}
