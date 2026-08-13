using System.Text;

using Carina.Infrastructure.Driver;

namespace Carina.Infrastructure.Tests;

public sealed class SseFramesTests
{
    private static async Task<IReadOnlyList<string>> NamesIn(string wire)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        var names = new List<string>();

        await foreach (var name in SseFrames.ReadNamesAsync(stream))
        {
            names.Add(name);
        }

        return names;
    }

    [Fact]
    public async Task SplitsFramesOnBlankLines()
    {
        var names = await NamesIn("event: tuners\ndata: tuners\n\nevent: sessions\ndata: sessions\n\n");

        Assert.Equal(["tuners", "sessions"], names);
    }

    [Fact]
    public async Task IgnoresCommentsAndUnknownFields()
    {
        var names = await NamesIn(": ping\nretry: 100\nevent: draining\ndata: whatever\nid: 7\n\n");

        Assert.Equal(["draining"], names);
    }

    [Fact]
    public async Task AFrameWithoutAnEventNameYieldsNothing()
    {
        Assert.Empty(await NamesIn("data: something\n\n"));
    }

    [Fact]
    public async Task AnEmptyEventNameYieldsNothing()
    {
        Assert.Empty(await NamesIn("event:\n\n"));
    }

    [Fact]
    public async Task ToleratesCarriageReturns()
    {
        var names = await NamesIn("event: tuners\r\ndata: tuners\r\n\r\n");

        Assert.Equal(["tuners"], names);
    }

    [Fact]
    public async Task AnUnterminatedFrameAtEndOfStreamIsNotDelivered()
    {
        Assert.Empty(await NamesIn("event: tuners\ndata: tuners\n"));
    }

    [Fact]
    public async Task UnknownNamesAreStillHandedUpForTheCallerToJudge()
    {
        var names = await NamesIn("event: somethingNew\n\n");

        Assert.Equal(["somethingNew"], names);
    }
}
