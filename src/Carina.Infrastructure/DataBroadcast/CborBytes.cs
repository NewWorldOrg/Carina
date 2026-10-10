using System.Buffers.Binary;
using System.Text;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The few CBOR items (RFC 8949) the catalog of a data broadcast is written in: unsigned integers, text,
/// booleans, and maps and arrays of a length told up front.
/// </summary>
internal sealed class CborBytes
{
    private const byte UnsignedMajor = 0;

    private const byte TextMajor = 3;

    private const byte ArrayMajor = 4;

    private const byte MapMajor = 5;

    private const byte False = 0xF4;

    private const byte True = 0xF5;

    private const ulong MostInHead = 23;

    private const byte OneByteFollows = 24;

    private const byte TwoBytesFollow = 25;

    private const byte FourBytesFollow = 26;

    private const byte EightBytesFollow = 27;

    private readonly List<byte> written = [];

    public CborBytes Unsigned(ulong value) => Head(UnsignedMajor, value);

    public CborBytes Text(string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);

        Head(TextMajor, (ulong)encoded.Length);
        written.AddRange(encoded);

        return this;
    }

    public CborBytes Boolean(bool value)
    {
        written.Add(value ? True : False);

        return this;
    }

    public CborBytes Array(int count) => Head(ArrayMajor, (ulong)count);

    public CborBytes Map(int pairs) => Head(MapMajor, (ulong)pairs);

    public byte[] ToArray() => [.. written];

    private CborBytes Head(byte major, ulong argument)
    {
        byte initial = (byte)(major << 5);

        if (argument <= MostInHead)
        {
            written.Add((byte)(initial | (byte)argument));

            return this;
        }

        Span<byte> following = stackalloc byte[sizeof(ulong)];
        int length = Following(argument, following);

        written.Add((byte)(initial | AdditionalFor(length)));
        written.AddRange(following[..length]);

        return this;
    }

    private static int Following(ulong argument, Span<byte> into)
    {
        if (argument <= byte.MaxValue)
        {
            into[0] = (byte)argument;

            return sizeof(byte);
        }

        if (argument <= ushort.MaxValue)
        {
            BinaryPrimitives.WriteUInt16BigEndian(into, (ushort)argument);

            return sizeof(ushort);
        }

        if (argument <= uint.MaxValue)
        {
            BinaryPrimitives.WriteUInt32BigEndian(into, (uint)argument);

            return sizeof(uint);
        }

        BinaryPrimitives.WriteUInt64BigEndian(into, argument);

        return sizeof(ulong);
    }

    private static byte AdditionalFor(int length)
        => length switch
        {
            sizeof(byte) => OneByteFollows,
            sizeof(ushort) => TwoBytesFollow,
            sizeof(uint) => FourBytesFollow,
            _ => EightBytesFollow,
        };
}
