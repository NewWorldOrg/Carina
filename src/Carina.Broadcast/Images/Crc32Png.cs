namespace Carina.Broadcast.Images;

public static class Crc32Png
{
    public const uint Polynomial = 0xEDB8_8320;

    public const uint Seed = 0xFFFF_FFFF;

    public const int ChecksumSize = 4;

    private static readonly uint[] Register = BuildRegister();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = Seed;

        foreach (byte octet in data)
        {
            crc = Register[(crc ^ octet) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ Seed;
    }

    private static uint[] BuildRegister()
    {
        uint[] register = new uint[256];

        for (int index = 0; index < register.Length; index++)
        {
            uint value = (uint)index;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }

            register[index] = value;
        }

        return register;
    }
}
