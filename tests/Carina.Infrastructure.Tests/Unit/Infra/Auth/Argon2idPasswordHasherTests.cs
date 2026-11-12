using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class Argon2idPasswordHasherTests
{
    private static readonly PasswordHashPolicy Weaker = new(
        PasswordHashPolicy.LeastMemoryKibibytes,
        5,
        1,
        PasswordHashPolicy.LeastSaltLength,
        PasswordHashPolicy.LeastDigestLength);

    private readonly Argon2idPasswordHasher hasher = new();

    [Fact]
    public void APasswordMatchesTheHashItWasMadeFrom()
    {
        PasswordHash hash = hasher.Hash("the right password", PasswordHashPolicy.Default);

        Assert.True(hasher.Matches("the right password", hash));
    }

    [Theory]
    [InlineData("the right password ")]
    [InlineData("The right password")]
    [InlineData("something else entirely")]
    public void AnotherPasswordDoesNotMatch(string offered)
    {
        PasswordHash hash = hasher.Hash("the right password", PasswordHashPolicy.Default);

        Assert.False(hasher.Matches(offered, hash));
    }

    [Fact]
    public void TheSamePasswordHashedTwiceIsStoredDifferentlyBecauseTheSaltIsFresh()
    {
        PasswordHash first = hasher.Hash("the right password", PasswordHashPolicy.Default);
        PasswordHash second = hasher.Hash("the right password", PasswordHashPolicy.Default);

        Assert.NotEqual(first.Value, second.Value);
        Assert.True(hasher.Matches("the right password", second));
    }

    [Fact]
    public void TheStoredHashCarriesTheCostItWasMadeWithAndSatisfiesTheStandingPolicy()
    {
        PasswordHash hash = hasher.Hash("the right password", PasswordHashPolicy.Default);

        Assert.Equal(PasswordHashPolicy.Default.MemoryKibibytes, hash.MemoryKibibytes);
        Assert.Equal(PasswordHashPolicy.Default.Iterations, hash.Iterations);
        Assert.Equal(PasswordHashPolicy.Default.Parallelism, hash.Parallelism);
        Assert.Equal(PasswordHashPolicy.Default.DigestLength, hash.DigestLength);
        Assert.StartsWith($"${PasswordHash.Algorithm}$v={PasswordHash.Version}$", hash.Value, StringComparison.Ordinal);
        Assert.False(PasswordHashPolicy.Default.NeedsRehash(hash));
    }

    [Fact]
    public void AHashMadeUnderAWeakerCostIsCheckedUnderThatCostRatherThanTheCurrentOne()
    {
        PasswordHash weaker = hasher.Hash("the right password", Weaker);

        Assert.Equal(Weaker.MemoryKibibytes, weaker.MemoryKibibytes);
        Assert.True(hasher.Matches("the right password", weaker));
        Assert.True(PasswordHashPolicy.Default.NeedsRehash(weaker));
    }

    [Fact]
    public void TheStoredHashNeverCarriesThePasswordItself()
    {
        PasswordHash hash = hasher.Hash("the right password", PasswordHashPolicy.Default);

        Assert.DoesNotContain("the right password", hash.Value, StringComparison.Ordinal);
        Assert.Equal("(password hash)", hash.ToString());
    }
}
