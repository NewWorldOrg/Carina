using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DevicePathContainmentTests : IDisposable
{
    private readonly string work = Directory.CreateTempSubdirectory("carina-paths-").FullName;

    private readonly string root;
    private readonly string outside;

    public DevicePathContainmentTests()
    {
        root = Path.Combine(work, "root");
        outside = Path.Combine(work, "outside");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "target"), []);
    }

    public void Dispose() => Directory.Delete(work, recursive: true);

    private string Root => root + Path.DirectorySeparatorChar;

    [Fact]
    public void AnHonestNodeUnderTheRootIsAccepted()
    {
        Directory.CreateDirectory(Path.Combine(root, "adapter0"));
        File.WriteAllBytes(Path.Combine(root, "adapter0", "frontend0"), []);

        Assert.True(
            DriverConfigurationReader.IsUnderRoot(
                Path.Combine(root, "adapter0", "frontend0"),
                Root
            )
        );
    }

    [Fact]
    public void ALeafSymbolicLinkOutOfTheRootIsRejected()
    {
        string link = Path.Combine(root, "frontend0");
        File.CreateSymbolicLink(link, Path.Combine(outside, "target"));

        Assert.False(DriverConfigurationReader.IsUnderRoot(link, Root));
    }

    [Fact]
    public void ASymbolicLinkAnyLevelAboveTheNodeIsRejected()
    {
        string branch = Path.Combine(root, "adapter0");
        Directory.CreateSymbolicLink(branch, outside);

        Assert.False(
            DriverConfigurationReader.IsUnderRoot(Path.Combine(branch, "frontend0"), Root)
        );
    }

    [Fact]
    public void ARelativePathIsRejected()
    {
        Assert.False(DriverConfigurationReader.IsUnderRoot("adapter0/frontend0", Root));
    }

    [Fact]
    public void TheRootItselfIsNotANode()
    {
        Assert.False(DriverConfigurationReader.IsUnderRoot(root, Root));
    }

    [Fact]
    public void APathOutsideTheRootIsRejected()
    {
        Assert.False(DriverConfigurationReader.IsUnderRoot(Path.Combine(outside, "target"), Root));
    }

    [Fact]
    public void ADeviceNodeUnderDevIsAcceptedByTheReader()
    {
        Assert.DoesNotContain(
            Problems("/dev/dvb/adapter0/frontend0"),
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    [Fact]
    public void ADeviceNodeOutsideDevIsRejectedByTheReader()
    {
        Assert.Contains(
            Problems(Path.Combine(outside, "target")),
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    private static IReadOnlyList<string> Problems(string devicePath) =>
        DriverConfigurationReader
            .Read($$"""
                {
                  "socketPath": "/run/carina/driver.sock",
                  "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
                  "shutdownGraceHours": 6,
                  "tuner": { "backend": "dvb" },
                  "devices": [
                    {
                      "id": "adapter0",
                      "kind": "terrestrial",
                      "devicePath": "{{devicePath}}",
                      "enabled": true
                    }
                  ]
                }
                """)
            .Problems;
}
