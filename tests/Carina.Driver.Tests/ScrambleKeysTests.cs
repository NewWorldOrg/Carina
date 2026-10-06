using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class ScrambleKeysTests
{
    private static readonly byte[] SyntheticPair =
    [
        0x01, 0x12, 0x23, 0x34, 0x45, 0x56, 0x67, 0x78,
        0x89, 0x9A, 0xAB, 0xBC, 0xCD, 0xDE, 0xEF, 0xF0,
    ];

    [Fact]
    public void TheOddKeyIsTheFirstHalfOfThePairAndTheEvenKeyTheSecond()
    {
        ScrambleKeys keys = ScrambleKeys.Of(SyntheticCardKeys.SystemKey, SyntheticCardKeys.InitialValue, SyntheticPair);
        byte[] plain = [.. Enumerable.Range(0, 184).Select(index => (byte)index)];

        byte[] odd = [.. plain];
        Multi2.Keyed(SyntheticCardKeys.SystemKey, SyntheticPair.AsSpan(0, 8)).EncryptPayload(odd, SyntheticCardKeys.InitialValueAsBlock);
        keys.Unscramble(odd, withOddKey: true);

        byte[] even = [.. plain];
        Multi2.Keyed(SyntheticCardKeys.SystemKey, SyntheticPair.AsSpan(8)).EncryptPayload(even, SyntheticCardKeys.InitialValueAsBlock);
        keys.Unscramble(even, withOddKey: false);

        Assert.Equal(plain, odd);
        Assert.Equal(plain, even);
    }

    [Theory]
    [InlineData(7, 16)]
    [InlineData(8, 15)]
    [InlineData(8, 17)]
    public void APairOrAnInitialValueOfTheWrongLengthIsRefused(int initialLength, int pairLength)
    {
        Assert.Throws<ArgumentException>(() =>
            ScrambleKeys.Of(SyntheticCardKeys.SystemKey, new byte[initialLength], new byte[pairLength])
        );
    }
}
