using Carina.Domain.Integrity;

using static Carina.Domain.Tests.Integrity.IntegrityFixtures;

namespace Carina.Domain.Tests.Integrity;

public sealed class IntegritySettingsTests
{
    [Fact]
    public void SettingsNobodyTouchedWalkNothing()
    {
        Assert.False(new IntegritySettings().WalksAnything);
    }

    [Fact]
    public void SettingsNobodyTouchedHoldTheSweepBackForAWhileAfterEachOne()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new IntegritySettings().BetweenManualSweeps);
    }

    [Fact]
    public void SettingsNobodyTouchedWalkTheFilesOnceADay()
    {
        Assert.Equal(TimeSpan.FromDays(1), new IntegritySettings().BetweenSweeps);
    }

    [Fact]
    public void SettingsNobodyTouchedWalkSoonAfterStartingRatherThanWaitingOutTheWholeDay()
    {
        var settings = new IntegritySettings();

        Assert.Equal(TimeSpan.FromMinutes(5), settings.BeforeFirstSweep);
        Assert.True(settings.BeforeFirstSweep < settings.BetweenSweeps);
    }

    [Fact]
    public void TheGapBetweenSweepsAskedForIsTheOneThatWasSet()
    {
        var settings = new IntegritySettings { BetweenSweeps = TimeSpan.FromHours(2) };

        Assert.Equal(TimeSpan.FromHours(2), settings.BetweenSweeps);
    }

    [Fact]
    public void TheWaitBeforeTheFirstSweepAskedForIsTheOneThatWasSet()
    {
        var settings = new IntegritySettings { BeforeFirstSweep = TimeSpan.FromMinutes(1) };

        Assert.Equal(TimeSpan.FromMinutes(1), settings.BeforeFirstSweep);
    }

    [Fact]
    public void TheHoldBackBetweenSweepsAskedForByHandIsTheOneThatWasSet()
    {
        var settings = new IntegritySettings { BetweenManualSweeps = TimeSpan.FromMinutes(20) };

        Assert.Equal(TimeSpan.FromMinutes(20), settings.BetweenManualSweeps);
    }

    [Fact]
    public void SettingsThatNameAMountedRootWalkSomething()
    {
        var settings = new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
        };

        Assert.True(settings.WalksAnything);
    }

    [Fact]
    public void AMountedRootKeepsItsNameAndItsPath()
    {
        var mounted = new StorageRootPath(Primary, "/srv/recordings");

        Assert.Equal("primary", mounted.Root.Value);
        Assert.Equal("/srv/recordings", mounted.Path);
    }

    [Theory]
    [InlineData("srv/recordings")]
    [InlineData("./recordings")]
    [InlineData("recordings")]
    public void APathThatIsNotAbsoluteIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StorageRootPath(Primary, path));
    }

    [Fact]
    public void APathOfOneSlashIsAbsoluteEnough()
    {
        Assert.Equal("/", new StorageRootPath(Primary, "/").Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AMountWithNoPathIsRefused(string path)
    {
        Assert.Throws<ArgumentException>(() => new StorageRootPath(Primary, path));
    }

    [Fact]
    public void AMountWithNoPathAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new StorageRootPath(Primary, null!));
    }

    [Fact]
    public void AMountWithNoRootIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new StorageRootPath(null!, "/srv/recordings"));
    }
}
