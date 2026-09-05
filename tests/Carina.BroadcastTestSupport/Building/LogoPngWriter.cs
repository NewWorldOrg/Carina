namespace Carina.BroadcastTestSupport;

public sealed class LogoPngWriter
{
    public const int Colours = 129;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public required int Width { get; init; }

    public required int Height { get; init; }

    public byte Index { get; init; }

    public bool CarriesThePalette { get; init; }

    public bool CorruptHeaderChecksum { get; init; }

    public byte BitDepth { get; init; } = 8;

    public byte ColourType { get; init; } = 3;

    public byte[] ToBytes()
    {
        var png = new List<byte>(Signature);

        png.AddRange(Header());

        if (CarriesThePalette)
        {
            png.AddRange(Chunk("PLTE"u8, Palette()));
            png.AddRange(Chunk("tRNS"u8, Opacities()));
        }

        png.AddRange(Chunk("IDAT"u8, Compressed(Raster())));
        png.AddRange(Chunk("IEND"u8, []));

        return png.ToArray();
    }

    private byte[] Header()
    {
        byte[] data =
        [
            .. Word((uint)Width),
            .. Word((uint)Height),
            BitDepth,
            ColourType,
            0,
            0,
            0,
        ];

        byte[] chunk = Chunk("IHDR"u8, data);

        if (CorruptHeaderChecksum)
        {
            chunk[^1] ^= 0xFF;
        }

        return chunk;
    }

    private byte[] Raster()
    {
        byte[] raster = new byte[Height * (Width + 1)];

        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                raster[(row * (Width + 1)) + 1 + column] = Index;
            }
        }

        return raster;
    }

    private static byte[] Palette()
    {
        byte[] palette = new byte[Colours * 3];

        for (int colour = 0; colour < Colours; colour++)
        {
            palette[colour * 3] = (byte)colour;
        }

        return palette;
    }

    private static byte[] Opacities()
    {
        byte[] opacities = new byte[Colours];

        Array.Fill(opacities, (byte)0xFF);

        return opacities;
    }

    private static byte[] Compressed(byte[] raw)
    {
        var stream = new List<byte> { 0x78, 0x01 };
        int at = 0;

        do
        {
            int take = Math.Min(0xFFFF, raw.Length - at);
            bool last = at + take == raw.Length;

            stream.Add((byte)(last ? 1 : 0));
            stream.Add((byte)(take & 0xFF));
            stream.Add((byte)(take >> 8));
            stream.Add((byte)(~take & 0xFF));
            stream.Add((byte)((~take >> 8) & 0xFF));
            stream.AddRange(raw.AsSpan(at, take).ToArray());

            at += take;
        }
        while (at < raw.Length);

        stream.AddRange(Word(Adler32(raw)));

        return stream.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint low = 1;
        uint high = 0;

        foreach (byte octet in data)
        {
            low = (low + octet) % 65521;
            high = (high + low) % 65521;
        }

        return (high << 16) | low;
    }

    private static byte[] Chunk(ReadOnlySpan<byte> type, byte[] data)
    {
        byte[] typed = [.. type, .. data];

        return [.. Word((uint)data.Length), .. typed, .. Word(ReferencePngCrc32.Compute(typed))];
    }

    private static byte[] Word(uint value)
        => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
