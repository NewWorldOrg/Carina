using Carina.Domain.Integrity;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class IntegrityOptionsTests
{
    [Fact]
    public void SettingsNobodyWroteFallBackToTheOnesTheCodeHolds()
    {
        IntegritySettings read = Read(new Dictionary<string, string?>());

        Assert.Equal(TimeSpan.FromMinutes(5), read.BeforeFirstSweep);
        Assert.Equal(TimeSpan.FromHours(6), read.BetweenSweeps);
        Assert.Empty(read.OutputRoots);
        Assert.False(read.WalksAnything);
    }

    [Fact]
    public void EverySettingThatIsWrittenIsTheOneThatIsUsed()
    {
        IntegritySettings read = Read(new Dictionary<string, string?>
        {
            ["Integrity:BeforeFirstSweep"] = "00:02:00",
            ["Integrity:BetweenSweeps"] = "1.00:00:00",
            ["Integrity:OutputRoots"] = "primary=/srv/recordings",
        });

        Assert.Equal(TimeSpan.FromMinutes(2), read.BeforeFirstSweep);
        Assert.Equal(TimeSpan.FromDays(1), read.BetweenSweeps);
        Assert.Equal("primary", Assert.Single(read.OutputRoots).Root.Value);
        Assert.Equal("/srv/recordings", read.OutputRoots[0].Path);
    }

    [Fact]
    public void SeveralRootsAreReadInTheOrderTheyWereWritten()
    {
        IntegritySettings read = Read(new Dictionary<string, string?>
        {
            ["Integrity:OutputRoots"] = " primary=/srv/recordings ; bulk=/mnt/bulk ",
        });

        Assert.Equal(["primary", "bulk"], read.OutputRoots.Select(mounted => mounted.Root.Value).ToArray());
        Assert.Equal(["/srv/recordings", "/mnt/bulk"], read.OutputRoots.Select(mounted => mounted.Path).ToArray());
    }

    [Fact]
    public void ATrailingSeparatorMountsNothingExtra()
    {
        IntegritySettings read = Read(new Dictionary<string, string?>
        {
            ["Integrity:OutputRoots"] = "primary=/srv/recordings;",
        });

        Assert.Single(read.OutputRoots);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    public void NoRootAtAllLeavesTheSweepWithNothingToWalk(string written)
    {
        IntegritySettings read = Read(new Dictionary<string, string?> { ["Integrity:OutputRoots"] = written });

        Assert.Empty(read.OutputRoots);
        Assert.False(read.WalksAnything);
    }

    [Fact]
    public void ARootThatNamesNoPathIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Read(new Dictionary<string, string?> { ["Integrity:OutputRoots"] = "primary" }));
    }

    [Fact]
    public void ARootMountedAtARelativePathIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Read(new Dictionary<string, string?> { ["Integrity:OutputRoots"] = "primary=srv/recordings" }));
    }

    [Fact]
    public void ARootWhoseNameIsNotOneTheLedgerCouldHoldIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Read(new Dictionary<string, string?> { ["Integrity:OutputRoots"] = "../etc=/srv/recordings" }));
    }

    [Fact]
    public void ARootMountedTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Read(new Dictionary<string, string?>
            {
                ["Integrity:OutputRoots"] = "primary=/srv/one;primary=/srv/two",
            }));
    }

    [Fact]
    public void TwoRootsAtTheSamePathAreBothKept()
    {
        IntegritySettings read = Read(new Dictionary<string, string?>
        {
            ["Integrity:OutputRoots"] = "primary=/srv/one;bulk=/srv/one",
        });

        Assert.Equal(2, read.OutputRoots.Count);
    }

    [Theory]
    [InlineData("Integrity:BeforeFirstSweep")]
    [InlineData("Integrity:BetweenSweeps")]
    public void AWaitThatIsNotADurationIsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> { [key] = "soon" }));
    }

    [Theory]
    [InlineData("Integrity:BeforeFirstSweep")]
    [InlineData("Integrity:BetweenSweeps")]
    public void AWaitOfNoTimeAtAllIsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> { [key] = "00:00:00" }));
    }

    [Theory]
    [InlineData("Integrity:BeforeFirstSweep")]
    [InlineData("Integrity:BetweenSweeps")]
    public void AWaitOfTheShortestMomentIsAllowed(string key)
    {
        Assert.NotNull(Read(new Dictionary<string, string?> { [key] = "00:00:00.0010000" }));
    }

    [Theory]
    [InlineData("Integrity:BeforeFirstSweep")]
    [InlineData("Integrity:BetweenSweeps")]
    public void AWaitThatRunsBackwardsIsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> { [key] = "-00:01:00" }));
    }

    [Fact]
    public void ValidationPassesSettingsItCanRead()
    {
        Assert.Equal(
            ValidateOptionsResult.Success,
            new IntegrityValidation().Validate(null, Options(new Dictionary<string, string?>
            {
                ["Integrity:OutputRoots"] = "primary=/srv/recordings",
            })));
    }

    [Fact]
    public void ValidationFailsSettingsItCannotRead()
    {
        ValidateOptionsResult result = new IntegrityValidation().Validate(
            null,
            Options(new Dictionary<string, string?> { ["Integrity:OutputRoots"] = "primary" }));

        Assert.True(result.Failed);
    }

    [Fact]
    public void ValidationWithNoSettingsAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new IntegrityValidation().Validate(null, null!));
    }

    [Fact]
    public void ReadingFromNoConfigurationAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new IntegrityOptions().ReadFrom(null!));
    }

    private static IntegritySettings Read(Dictionary<string, string?> settings) => Options(settings).Read();

    private static IntegrityOptions Options(Dictionary<string, string?> settings)
    {
        var options = new IntegrityOptions();
        options.ReadFrom(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return options;
    }
}
