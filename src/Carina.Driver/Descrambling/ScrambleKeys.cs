using System.Buffers.Binary;

namespace Carina.Driver.Descrambling;

/// <summary>
/// The odd and the even scramble key one ECM unlocked, keyed and ready to unscramble payloads.
/// </summary>
public sealed class ScrambleKeys
{
    public const int PairLength = 2 * Multi2.DataKeyLength;

    public const int InitialValueLength = 8;

    private readonly Multi2 odd;

    private readonly Multi2 even;

    private readonly ulong initialValue;

    private ScrambleKeys(Multi2 odd, Multi2 even, ulong initialValue)
    {
        this.odd = odd;
        this.even = even;
        this.initialValue = initialValue;
    }

    /// <summary>
    /// Keys a pair answered for an ECM: the odd key in the first eight bytes, the even key in the next eight.
    /// </summary>
    public static ScrambleKeys Of(
        ReadOnlySpan<byte> systemKey,
        ReadOnlySpan<byte> initialValue,
        ReadOnlySpan<byte> pair
    )
    {
        if (initialValue.Length is not InitialValueLength)
        {
            throw new ArgumentException(
                $"An initial value is {InitialValueLength} bytes, not {initialValue.Length}.",
                nameof(initialValue)
            );
        }

        if (pair.Length is not PairLength)
        {
            throw new ArgumentException($"A key pair is {PairLength} bytes, not {pair.Length}.", nameof(pair));
        }

        return new ScrambleKeys(
            Multi2.Keyed(systemKey, pair[..Multi2.DataKeyLength]),
            Multi2.Keyed(systemKey, pair[Multi2.DataKeyLength..]),
            BinaryPrimitives.ReadUInt64BigEndian(initialValue)
        );
    }

    public void Unscramble(Span<byte> payload, bool withOddKey) =>
        (withOddKey ? odd : even).DecryptPayload(payload, initialValue);

    public override string ToString() => nameof(ScrambleKeys);
}
