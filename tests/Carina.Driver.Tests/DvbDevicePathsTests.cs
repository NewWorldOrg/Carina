using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbDevicePathsTests
{
    [Fact]
    public void TheDemuxAndReaderSitBesideTheFrontendUnderTheSameAdapter()
    {
        Assert.True(
            DvbDevicePaths.TryDerive("/dev/dvb/adapter0/frontend0", out var paths, out _)
        );
        Assert.Equal("/dev/dvb/adapter0/demux0", paths.Demux);
        Assert.Equal("/dev/dvb/adapter0/dvr0", paths.Dvr);
    }

    [Fact]
    public void TheNodeIndexIsCarriedAcrossFromTheFrontend()
    {
        Assert.True(
            DvbDevicePaths.TryDerive("/dev/dvb/adapter3/frontend1", out var paths, out _)
        );
        Assert.Equal("/dev/dvb/adapter3/demux1", paths.Demux);
        Assert.Equal("/dev/dvb/adapter3/dvr1", paths.Dvr);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingFrontendPathIsNamedRatherThanAssumed(string? frontendPath)
    {
        Assert.False(DvbDevicePaths.TryDerive(frontendPath, out _, out var problem));
        Assert.Contains("devicePath", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dev/video0")]
    [InlineData("/etc/passwd")]
    [InlineData("/dev/dvbadapter0/frontend0")]
    public void APathOutsideTheDvbTreeIsRefused(string frontendPath)
    {
        Assert.False(DvbDevicePaths.TryDerive(frontendPath, out _, out var problem));
        Assert.Contains("/dev/dvb/", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dev/dvb/adapter0/demux0")]
    [InlineData("/dev/dvb/adapter0/frontend")]
    [InlineData("/dev/dvb/adapter0/frontendA")]
    public void ANodeThatIsNotANumberedFrontendIsRefused(string frontendPath)
    {
        Assert.False(DvbDevicePaths.TryDerive(frontendPath, out _, out var problem));
        Assert.Contains("frontendN", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrontendNotInsideAnAdapterDirectoryIsRefused()
    {
        Assert.False(DvbDevicePaths.TryDerive("/dev/dvb/frontend0", out _, out var problem));
        Assert.Contains("adapterN", problem, StringComparison.Ordinal);
    }
}
