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
