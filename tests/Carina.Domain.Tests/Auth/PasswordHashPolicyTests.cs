using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PasswordHashPolicyTests
{
    [Fact]
    public void TheDefaultPolicyAsksForTheMemoryThatMakesTheHashHardToBuyThroughput()
    {
        PasswordHashPolicy policy = PasswordHashPolicy.Default;

        Assert.Equal(19456, policy.MemoryKibibytes);
        Assert.Equal(2, policy.Iterations);
        Assert.Equal(1, policy.Parallelism);
        Assert.Equal(16, policy.SaltLength);
        Assert.Equal(32, policy.DigestLength);
    }

    [Fact]
    public void APolicyBuyingLessMemoryPaysForItInRoundsInstead()
    {
        var policy = new PasswordHashPolicy(12288, 3, 1, 16, 32);

        Assert.Equal(12288, policy.MemoryKibibytes);
    }

    [Fact]
    public void APolicyTooCheapToBeMemoryHardIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHashPolicy(4096, 2, 1, 16, 32));
    }

    [Fact]
    public void APolicyBuyingMemoryButSkippingTheRoundsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHashPolicy(8192, 1, 1, 16, 32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARoundCountThatIsNotACountIsRefused(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PasswordHashPolicy(19456, iterations, 1, 16, 32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALaneCountThatIsNotACountIsRefused(int parallelism)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PasswordHashPolicy(19456, 2, parallelism, 16, 32));
    }

    [Fact]
    public void ASaltShortEnoughToCollideAcrossInstallationsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHashPolicy(19456, 2, 1, 8, 32));
    }

    [Fact]
    public void ADigestShorterThanTheHashItStandsForIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHashPolicy(19456, 2, 1, 16, 16));
    }

    [Fact]
    public void AHashMadeUnderTheCurrentPolicyIsLeftAlone()
    {
        PasswordHash hash = Hash(PasswordHashPolicy.Default);

        Assert.False(PasswordHashPolicy.Default.NeedsRehash(hash));
    }

    [Fact]
    public void AHashMadeUnderAWeakerPolicyIsRemadeOnTheNextSuccessfulSignIn()
    {
        PasswordHash hash = Hash(new PasswordHashPolicy(12288, 3, 1, 16, 32));

        Assert.True(new PasswordHashPolicy(19456, 2, 1, 16, 32).NeedsRehash(hash));
    }

    [Fact]
    public void AHashMadeUnderAStrongerPolicyIsNotWeakenedToMatch()
    {
        PasswordHash hash = Hash(new PasswordHashPolicy(65536, 3, 1, 16, 32));

        Assert.False(new PasswordHashPolicy(19456, 2, 1, 16, 32).NeedsRehash(hash));
    }

    [Fact]
    public void AHashCarryingAShorterDigestThanTheCurrentPolicyIsRemade()
    {
        PasswordHash hash = Hash(new PasswordHashPolicy(65536, 3, 1, 16, 32));

        Assert.True(new PasswordHashPolicy(19456, 2, 1, 16, 64).NeedsRehash(hash));
    }

    [Fact]
    public void JudgingAHashNeedsAHashToJudge()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordHashPolicy.Default.NeedsRehash(null!));
    }

    [Fact]
    public void TwoPoliciesWithTheSameNumbersAreTheSamePolicy()
    {
        Assert.Equal(new PasswordHashPolicy(19456, 2, 1, 16, 32), PasswordHashPolicy.Default);
    }

    private static PasswordHash Hash(PasswordHashPolicy policy)
        => PasswordHash.Encode(
            policy,
            new byte[policy.SaltLength],
            new byte[policy.DigestLength]);
}
