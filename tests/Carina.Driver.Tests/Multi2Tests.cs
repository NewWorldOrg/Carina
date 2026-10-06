using System.Buffers.Binary;

using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class Multi2Tests
{
    private static readonly byte[] SyntheticSystemKey =
    [
        0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0xF0, 0xE1, 0xD2, 0xC3, 0xB4, 0xA5, 0x96, 0x87,
        0x78, 0x69, 0x5A, 0x4B, 0x3C, 0x2D, 0x1E, 0x0F,
    ];

    private static readonly byte[] SyntheticDataKey = [0xA5, 0x5A, 0x0F, 0xF0, 0x3C, 0xC3, 0x99, 0x66];

    private const ulong SyntheticInitialValue = 0x0F1E2D3C4B5A6978;

    /// <summary>The first vector of the MULTI2 self-test in libtomcrypt: 128 steps, which is 16 cycles of eight.</summary>
    [Fact]
    public void TheFirstPublishedVectorComesOut()
    {
        byte[] systemKey = new byte[Multi2.SystemKeyLength];
        byte[] dataKey = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

        Multi2 cipher = Multi2.Keyed(systemKey, dataKey, cycles: 16);

        Assert.Equal(0xF89440845E11CF89UL, cipher.Encrypt(0x0000000000000001UL));
        Assert.Equal(0x0000000000000001UL, cipher.Decrypt(0xF89440845E11CF89UL));
    }

    /// <summary>The second vector of the MULTI2 self-test in libtomcrypt: 216 steps, which is 27 cycles of eight.</summary>
    [Fact]
    public void TheSecondPublishedVectorComesOut()
    {
        byte[] systemKey =
        [
            0x35, 0x91, 0x9D, 0x96, 0x07, 0x02, 0xE2, 0xCE,
            0x8D, 0x0B, 0x58, 0x3C, 0xC9, 0xC8, 0x9D, 0x59,
            0xA2, 0xAE, 0x96, 0x4E, 0x87, 0x82, 0x45, 0xED,
            0x3F, 0x2E, 0x62, 0xD6, 0x36, 0x35, 0xD0, 0x67,
        ];
        byte[] dataKey = [0xB1, 0x27, 0xB9, 0x06, 0xE7, 0x56, 0x22, 0x38];

        Multi2 cipher = Multi2.Keyed(systemKey, dataKey, cycles: 27);

        Assert.Equal(0xCA84A93475C860E5UL, cipher.Encrypt(0x1FB46060D0B34FA5UL));
        Assert.Equal(0x1FB46060D0B34FA5UL, cipher.Decrypt(0xCA84A93475C860E5UL));
    }

    [Fact]
    public void TheBroadcastCycleCountIsFour()
    {
        Multi2 broadcast = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);
        Multi2 four = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey, cycles: 4);
        Multi2 five = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey, cycles: 5);

        Assert.Equal(four.Encrypt(0x0123456789ABCDEFUL), broadcast.Encrypt(0x0123456789ABCDEFUL));
        Assert.NotEqual(five.Encrypt(0x0123456789ABCDEFUL), broadcast.Encrypt(0x0123456789ABCDEFUL));
    }

    [Fact]
    public void EveryBlockComesBackFromWhatItWasEncryptedInto()
    {
        Random random = new(20261006);
        byte[] systemKey = new byte[Multi2.SystemKeyLength];
        byte[] dataKey = new byte[Multi2.DataKeyLength];

        for (int trial = 0; trial < 200; trial++)
        {
            random.NextBytes(systemKey);
            random.NextBytes(dataKey);
            ulong block = (ulong)random.NextInt64();

            Multi2 cipher = Multi2.Keyed(systemKey, dataKey);
            ulong scrambled = cipher.Encrypt(block);

            Assert.NotEqual(block, scrambled);
            Assert.Equal(block, cipher.Decrypt(scrambled));
        }
    }

    [Fact]
    public void AnotherDataKeyUnderTheSameSystemKeyScramblesDifferently()
    {
        byte[] otherDataKey = [.. SyntheticDataKey];
        otherDataKey[^1] ^= 0x01;

        Multi2 first = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);
        Multi2 second = Multi2.Keyed(SyntheticSystemKey, otherDataKey);

        Assert.NotEqual(first.Encrypt(0UL), second.Encrypt(0UL));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(176)]
    [InlineData(183)]
    [InlineData(184)]
    public void APayloadOfAnyLengthComesBackFromItsScrambledForm(int length)
    {
        byte[] plain = new byte[length];
        new Random(length).NextBytes(plain);
        byte[] payload = [.. plain];
        Multi2 cipher = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);

        cipher.EncryptPayload(payload, SyntheticInitialValue);
        if (length > 0)
        {
            Assert.NotEqual(plain, payload);
        }

        cipher.DecryptPayload(payload, SyntheticInitialValue);

        Assert.Equal(plain, payload);
    }

    [Fact]
    public void WholeBlocksAreChainedFromTheInitialValue()
    {
        byte[] payload = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        ulong first = BinaryPrimitives.ReadUInt64BigEndian(payload);
        ulong second = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(8));
        Multi2 cipher = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);

        cipher.EncryptPayload(payload, SyntheticInitialValue);

        ulong firstScrambled = cipher.Encrypt(first ^ SyntheticInitialValue);
        ulong secondScrambled = cipher.Encrypt(second ^ firstScrambled);
        Assert.Equal(firstScrambled, BinaryPrimitives.ReadUInt64BigEndian(payload));
        Assert.Equal(secondScrambled, BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(8)));
    }

    [Fact]
    public void AShortTailIsMaskedWithTheLastScrambledBlockEncryptedAgain()
    {
        byte[] payload = [0, 1, 2, 3, 4, 5, 6, 7, 0xAA, 0xBB, 0xCC];
        Multi2 cipher = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);

        cipher.EncryptPayload(payload, SyntheticInitialValue);

        ulong lastBlock = BinaryPrimitives.ReadUInt64BigEndian(payload);
        byte[] mask = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(mask, cipher.Encrypt(lastBlock));
        Assert.Equal([(byte)(0xAA ^ mask[0]), (byte)(0xBB ^ mask[1]), (byte)(0xCC ^ mask[2])], payload[8..]);
    }

    [Fact]
    public void APayloadShorterThanABlockIsMaskedWithTheInitialValueEncrypted()
    {
        byte[] payload = [0x11, 0x22, 0x33, 0x44, 0x55];
        Multi2 cipher = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);

        cipher.DecryptPayload(payload, SyntheticInitialValue);

        byte[] mask = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(mask, cipher.Encrypt(SyntheticInitialValue));
        Assert.Equal(
            [(byte)(0x11 ^ mask[0]), (byte)(0x22 ^ mask[1]), (byte)(0x33 ^ mask[2]), (byte)(0x44 ^ mask[3]), (byte)(0x55 ^ mask[4])],
            payload
        );
    }

    [Fact]
    public void ABrokenScrambledBlockSpoilsOnlyItselfAndTheBlockAfterIt()
    {
        byte[] plain = new byte[32];
        new Random(32).NextBytes(plain);
        byte[] payload = [.. plain];
        Multi2 cipher = Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey);
        cipher.EncryptPayload(payload, SyntheticInitialValue);

        payload[8] ^= 0x80;
        cipher.DecryptPayload(payload, SyntheticInitialValue);

        Assert.Equal(plain[..8], payload[..8]);
        Assert.NotEqual(plain[8..16], payload[8..16]);
        Assert.NotEqual(plain[16..24], payload[16..24]);
        Assert.Equal(plain[24..], payload[24..]);
    }

    [Fact]
    public void TheBroadcastModeGivesThePinnedBytes()
    {
        byte[] payload = new byte[19];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)index;
        }

        Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey).EncryptPayload(payload, SyntheticInitialValue);

        Assert.Equal(Convert.FromHexString(PinnedBroadcastBytes), payload);
    }

    private const string PinnedBroadcastBytes = "A5AE28161210F640DB27780CDCA9FEC9F17C09";

    [Theory]
    [InlineData(31, 8)]
    [InlineData(33, 8)]
    [InlineData(32, 7)]
    [InlineData(32, 9)]
    public void AKeyOfTheWrongLengthIsRefused(int systemLength, int dataLength)
    {
        Assert.Throws<ArgumentException>(() => Multi2.Keyed(new byte[systemLength], new byte[dataLength]));
    }

    [Fact]
    public void AtLeastOneCycleIsRun()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Multi2.Keyed(SyntheticSystemKey, SyntheticDataKey, cycles: 0)
        );
    }
}
