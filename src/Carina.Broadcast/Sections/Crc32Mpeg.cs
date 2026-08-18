namespace Carina.Broadcast.Sections;

public static class Crc32Mpeg
{
    public const uint Polynomial = 0x04C1_1DB7;

    public const uint Seed = 0xFFFF_FFFF;

    public const int ChecksumSize = 4;

    private static readonly uint[] Register = BuildRegister();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = Seed;

        foreach (byte octet in data)
        {
            crc = (crc << 8) ^ Register[((crc >> 24) ^ octet) & 0xFF];
        }

        return crc;
    }

    public static bool Verifies(ReadOnlySpan<byte> dataFollowedByItsChecksum)
    {
        if (dataFollowedByItsChecksum.Length < ChecksumSize)
        {
            return false;
        }

        ReadOnlySpan<byte> body = dataFollowedByItsChecksum[..^ChecksumSize];
        uint carried = ((uint)dataFollowedByItsChecksum[^4] << 24)
            | ((uint)dataFollowedByItsChecksum[^3] << 16)
            | ((uint)dataFollowedByItsChecksum[^2] << 8)
            | dataFollowedByItsChecksum[^1];

        return Compute(body) == carried;
    }

    private static uint[] BuildRegister()
    {
        uint[] register = new uint[256];

        for (int index = 0; index < register.Length; index++)
        {
            uint value = (uint)index << 24;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 0x8000_0000) != 0 ? (value << 1) ^ Polynomial : value << 1;
            }

            register[index] = value;
        }

        return register;
    }
}
