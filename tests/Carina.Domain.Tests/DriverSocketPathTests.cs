using Carina.Domain.DriverStatus;

namespace Carina.Domain.Tests;

public sealed class DriverSocketPathTests
{
    [Fact]
    public void AcceptsAnAbsolutePath()
    {
        Assert.Equal("/run/carina/driver.sock", new DriverSocketPath("/run/carina/driver.sock").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankPath(string value)
    {
        Assert.Throws<ArgumentException>(() => new DriverSocketPath(value));
    }

    [Fact]
    public void RejectsARelativePath()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DriverSocketPath("driver.sock"));

        Assert.Contains("absolute", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparesByValue()
    {
        Assert.Equal(new DriverSocketPath("/run/carina/driver.sock"), new DriverSocketPath("/run/carina/driver.sock"));
        Assert.NotEqual(new DriverSocketPath("/run/carina/driver.sock"), new DriverSocketPath("/tmp/driver.sock"));
    }
}
