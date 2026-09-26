using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeAutoRunTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly MachineSettings SixCores = new() { Cores = 6 };

    [Fact(DisplayName = "the auto-run settles on whether it runs and how many cores a run may take")]
    public void TheAutoRunSettlesOnWhetherItRunsAndHowManyCores()
    {
        EncodeAutoRun settled = EncodeAutoRun.Settled(false, 3, Noon);

        Assert.Equal(EncodeAutoRun.TheOnlyRow, settled.Id);
        Assert.False(settled.Automatically);
        Assert.Equal(3, settled.MostCores);
        Assert.Equal(Noon, settled.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EncodeAutoRun.MostCoresAnyMachineHas + 1)]
    public void ARunTakesAtLeastOneCoreAndNoMoreThanAnyMachineHas(int cores)
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeAutoRun.Settled(true, cores, Noon));

    [Fact]
    public void TheRowIsTimedInUtc()
        => Assert.Throws<ArgumentException>(
            () => EncodeAutoRun.Settled(true, 2, new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Unspecified)));

    [Fact(DisplayName = "what the auto-run takes is a recording that completed and one cut short, and nothing narrows it")]
    public void WhatTheAutoRunTakesIsWhatEndedWithAFile()
        => Assert.Equal([RecordingOutcome.Complete, RecordingOutcome.Truncated], EncodeAutoRun.Subject);

    [Theory]
    [InlineData(6, 6)]
    [InlineData(EncodeAutoRun.MostCoresAnyMachineHas + 40, EncodeAutoRun.MostCoresAnyMachineHas)]
    [InlineData(0, EncodeAutoRun.FewestCores)]
    [InlineData(-1, EncodeAutoRun.FewestCores)]
    public void TheCapAMachineOffersIsWhatItHasWithinWhatAnyMachineCanHold(int cores, int offered)
        => Assert.Equal(offered, EncodeAutoRun.CoresOn(new MachineSettings { Cores = cores }));

    [Fact(DisplayName = "a machine nobody has settled stands as it was deployed, and says so")]
    public void AMachineNobodyHasSettledStandsAsItWasDeployed()
    {
        EncodeAutoRunStanding standing = EncodeAutoRunStanding.Over(
            null,
            new EncodeSettings { Automatically = false, MostCores = 4 },
            SixCores);

        Assert.False(standing.Automatically);
        Assert.Equal(4, standing.MostCores);
        Assert.False(standing.Stored);
        Assert.Null(standing.UpdatedAt);
    }

    [Fact(DisplayName = "a settled row stands over what the machine was deployed with")]
    public void ASettledRowStandsOverWhatTheMachineWasDeployedWith()
    {
        EncodeAutoRunStanding standing = EncodeAutoRunStanding.Over(
            EncodeAutoRun.Settled(true, 1, Noon),
            new EncodeSettings { Automatically = false, MostCores = 4 },
            SixCores);

        Assert.True(standing.Automatically);
        Assert.Equal(1, standing.MostCores);
        Assert.True(standing.Stored);
        Assert.Equal(Noon, standing.UpdatedAt);
    }

    [Fact(DisplayName = "a deployed cap above what this machine has stands as what will actually run")]
    public void ADeployedCapAboveWhatThisMachineHasStandsAsWhatWillRun()
    {
        EncodeAutoRunStanding standing = EncodeAutoRunStanding.Over(
            null,
            new EncodeSettings { MostCores = 8 },
            SixCores);

        Assert.Equal(6, standing.MostCores);
        Assert.False(standing.Stored);
    }

    [Fact(DisplayName = "a settled cap above what this machine has stands as what will actually run")]
    public void ASettledCapAboveWhatThisMachineHasStandsAsWhatWillRun()
    {
        EncodeAutoRunStanding standing = EncodeAutoRunStanding.Over(
            EncodeAutoRun.Settled(true, EncodeAutoRun.MostCoresAnyMachineHas, Noon),
            new EncodeSettings { MostCores = 2 },
            SixCores);

        Assert.Equal(6, standing.MostCores);
        Assert.True(standing.Stored);
    }

    [Fact(DisplayName = "a deployed cap of nought is still a core, because a run cannot have none")]
    public void ADeployedCapOfNoughtIsStillACore()
        => Assert.Equal(
            EncodeAutoRun.FewestCores,
            EncodeAutoRunStanding.Over(null, new EncodeSettings { MostCores = 0 }, SixCores).MostCores);

    [Fact(DisplayName = "a machine that says it has no core at all still offers one")]
    public void AMachineThatSaysItHasNoCoreAtAllStillOffersOne()
        => Assert.Equal(
            EncodeAutoRun.FewestCores,
            EncodeAutoRunStanding.Over(null, new EncodeSettings { MostCores = 4 }, new MachineSettings { Cores = 0 }).MostCores);
}
