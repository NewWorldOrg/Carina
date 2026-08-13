using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DevicePathContainmentTests : IDisposable
{
    private readonly string outside = Directory
        .CreateTempSubdirectory("carina-outside-")
        .FullName;

    private readonly List<string> links = [];

    public void Dispose()
    {
        foreach (var link in links)
        {
            File.Delete(link);
        }

        Directory.Delete(outside, recursive: true);
    }

    private string LinkUnder(string root, string name, string target)
    {
        var link = Path.Combine(root, name);
        File.CreateSymbolicLink(link, target);
        links.Add(link);

        return link;
    }

    private static IReadOnlyList<string> Problems(string devicePath) =>
        DriverConfigurationReader
            .Read($$"""
                {
                  "socketPath": "/run/carina/driver.sock",
                  "recordingsDirectory": "/srv/recordings",
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

    [Fact]
    public void AnHonestDeviceNodeIsAccepted()
    {
        Assert.Empty(Problems("/dev/dvb/adapter0/frontend0"));
    }

    [Fact]
    public void ALeafSymbolicLinkOutOfDevIsRejected()
    {
        if (!CanWriteToDev())
        {
            return;
        }

        var link = LinkUnder("/dev", "carina-test-leaf", Path.Combine(outside, "target"));

        Assert.Contains(
            Problems(link),
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    [Fact]
    public void ASymbolicLinkAnyLevelAboveTheDeviceIsRejected()
    {
        if (!CanWriteToDev())
        {
            return;
        }

        var link = LinkUnder("/dev", "carina-test-branch", outside);

        Assert.Contains(
            Problems($"{link}/adapter0/frontend0"),
            problem => problem.StartsWith("devices[0].devicePath:")
        );
    }

    private static bool CanWriteToDev()
    {
        try
        {
            var probe = Path.Combine("/dev", $"carina-probe-{Guid.NewGuid():N}");
            File.CreateSymbolicLink(probe, "/tmp");
            File.Delete(probe);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
