using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Domain.Tests.Scans;

public sealed class ScanScopeTests
{
    [Fact]
    public void AScopeOverNamedTargetsCoversOnlyTheSystemsThoseTargetsAreOn()
    {
        var scope = ScanScope.Over([TuningParameters.Terrestrial(53), TuningParameters.Terrestrial(55)]);

        Assert.True(scope.Covers(TuneSystem.IsdbT));
        Assert.False(scope.Covers(TuneSystem.IsdbSBs));
        Assert.False(scope.Covers(TuneSystem.IsdbSCs110));
    }

    [Fact]
    public void ANamedTargetRepeatedIsWalkedOnce()
    {
        var scope = ScanScope.Over(
            [TuningParameters.Terrestrial(53), TuningParameters.Terrestrial(53)]);

        Assert.Single(scope.NamedTargets);
    }

    [Fact]
    public void AScopeNamingSystemsLeavesTheTargetsToBeEnumerated()
    {
        var scope = ScanScope.Of(TuneSystem.IsdbT);

        Assert.False(scope.NamesItsOwnTargets);
        Assert.Empty(scope.NamedTargets);
    }

    [Fact]
    public void EverythingCoversTheThreeSystemsThatCanBeExpressed()
    {
        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            ScanScope.Everything.Systems);
    }

    [Fact]
    public void AScopeCoveringNothingIsRefusedRatherThanWalkingEveryChannel()
    {
        Assert.Throws<ArgumentException>(() => ScanScope.Of());
        Assert.Throws<ArgumentException>(() => ScanScope.Over([]));
    }

    [Fact]
    public void ASystemThisBuildCannotNameIsRefusedInsteadOfBeingSkippedQuietly()
    {
        Assert.Throws<ArgumentException>(() => ScanScope.Of(TuneSystem.Unspecified));
    }
}
