using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class StationLogoTests
{
    private static readonly DateTime At = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ALogoIsIdentifiedByItsNetworkAndItsLogoIdAlone()
    {
        StationLogo logo = Collected();

        Assert.Equal(new NetworkId(32741), logo.NetworkId);
        Assert.Equal(new LogoId(261), logo.LogoId);
        Assert.Equal(64 * 36, logo.Area);
        Assert.Equal(At, logo.CollectedAt);
    }

    [Fact]
    public void ALargerDrawingOfTheSameLogoTakesTheSmallerOnesPlace()
    {
        StationLogo held = Collected(width: 48, height: 24);

        Assert.True(held.Absorb(Collected(width: 64, height: 36, at: At.AddDays(1))));
        Assert.Equal(64, held.Width);
        Assert.Equal(36, held.Height);
        Assert.Equal(At.AddDays(1), held.CollectedAt);
    }

    [Fact]
    public void ASmallerDrawingOfTheSameLogoIsNotTakenOverTheOneAlreadyHeld()
    {
        StationLogo held = Collected(width: 64, height: 36);

        Assert.False(held.Absorb(Collected(width: 48, height: 24, at: At.AddDays(1))));
        Assert.Equal(64, held.Width);
        Assert.Equal(At, held.CollectedAt);
    }

    [Fact]
    public void ANewVersionOfTheSameDrawingReplacesTheOneHeld()
    {
        StationLogo held = Collected(version: 3);

        Assert.True(held.Absorb(Collected(version: 4, at: At.AddDays(1))));
        Assert.Equal(4, held.LogoVersion);
        Assert.Equal(At.AddDays(1), held.CollectedAt);
    }

    [Fact]
    public void SeeingTheSameDrawingAtTheSameVersionAgainChangesNothingAtAll()
    {
        StationLogo held = Collected(version: 3);

        Assert.False(held.Absorb(Collected(version: 3, at: At.AddDays(30))));
        Assert.Equal(At, held.CollectedAt);
    }

    [Fact]
    public void ALogoWithNoPictureInItIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Collected(picture: []));
    }

    [Fact]
    public void ALogoMeasuringNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Collected(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Collected(height: 0));
    }

    [Fact]
    public void ATimeThatIsNotInUniversalTimeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Collected(at: new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Local)));
    }

    [Theory]
    [InlineData(LogoId.MaxValue + 1)]
    [InlineData(-1)]
    public void ALogoIdOutsideTheNineBitsTheBroadcastCarriesIsRefused(int logoId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogoId(logoId));
    }

    private static StationLogo Collected(
        int width = 64,
        int height = 36,
        int version = 3,
        byte[]? picture = null,
        DateTime? at = null)
        => StationLogo.Collect(
            new NetworkId(32741),
            new LogoId(261),
            0x05,
            version,
            width,
            height,
            picture ?? [0x89, 0x50, 0x4E, 0x47],
            at ?? At);
}
