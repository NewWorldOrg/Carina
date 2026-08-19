using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PasswordHashTests
{
    private static readonly byte[] Salt =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Fact]
    public void AnEncodedHashNamesTheAlgorithmAndTheCostItWasMadeAt()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredHashReadsBackTheParametersItWasMadeWith()
    {
        PasswordHash written = PasswordHash.Encode(new PasswordHashPolicy(65536, 3, 2, 16, 32), Salt, Digest(0xAB));

        var read = new PasswordHash(written.Value);

        Assert.Equal(65536, read.MemoryKibibytes);
        Assert.Equal(3, read.Iterations);
        Assert.Equal(2, read.Parallelism);
        Assert.Equal(32, read.DigestLength);
    }

    [Fact]
    public void AStoredHashHandsBackTheSaltTheDerivationHasToRepeat()
    {
        PasswordHash hash = new(PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB)).Value);

        Assert.Equal(Salt, hash.CopySalt());
    }

    [Fact]
    public void TheSaltHandedBackIsACopyRatherThanTheHashesOwn()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        byte[] taken = hash.CopySalt();
        taken[0] = 0xFF;

        Assert.Equal(Salt, hash.CopySalt());
    }

    [Fact]
    public void ADerivationLandingOnTheStoredDigestIsTheRightPassword()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        Assert.True(hash.Matches(Digest(0xAB)));
    }

    [Fact]
    public void ADerivationLandingAnywhereElseIsTheWrongPassword()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        byte[] almost = Digest(0xAB);
        almost[^1] ^= 0x01;

        Assert.False(hash.Matches(almost));
    }

    [Fact]
    public void ADerivationOfTheWrongLengthIsTheWrongPassword()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        Assert.False(hash.Matches(new byte[16]));
    }

    [Fact]
    public void ANewSaltGivesTheSamePasswordADifferentRow()
    {
        PasswordHash first = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));
        PasswordHash second = PasswordHash.Encode(PasswordHashPolicy.Default, new byte[16], Digest(0xAB));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void EncodingRefusesASaltTheDerivationWouldNotHaveProduced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PasswordHash.Encode(PasswordHashPolicy.Default, new byte[8], Digest(0xAB)));
    }

    [Fact]
    public void EncodingRefusesADigestTheDerivationWouldNotHaveProduced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PasswordHash.Encode(PasswordHashPolicy.Default, Salt, new byte[16]));
    }

    [Fact]
    public void EncodingNeedsAPolicyToEncodeAgainst()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordHash.Encode(null!, Salt, Digest(0xAB)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$")]
    [InlineData("$argon2id$m=19456,t=2,p=1$c2FsdA$ZGlnZXN0")]
    public void ARowThatIsNotAnEncodedHashIsRefusedRatherThanTreatedAsOne(string value)
    {
        Assert.Throws<ArgumentException>(() => new PasswordHash(value));
    }

    [Fact]
    public void AHashMadeByAnAlgorithmWeDoNotRunIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new PasswordHash("$argon2i$v=19$m=19456,t=2,p=1$AQIDBAUGBwgJCgsMDQ4PEA$q80"));
    }

    [Fact]
    public void AHashFromAnArgonVersionWeDoNotRunIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new PasswordHash("$argon2id$v=16$m=19456,t=2,p=1$AQIDBAUGBwgJCgsMDQ4PEA$q80"));
    }

    [Fact]
    public void AHashIsNeverNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordHash(null!));
    }

    [Fact]
    public void AHashKeepsItsSecretOutOfWhateverPrintsIt()
    {
        PasswordHash hash = PasswordHash.Encode(PasswordHashPolicy.Default, Salt, Digest(0xAB));

        Assert.DoesNotContain("$", hash.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("$", $"{hash}", StringComparison.Ordinal);
    }

    private static byte[] Digest(byte fill) => [.. Enumerable.Repeat(fill, 32)];
}
