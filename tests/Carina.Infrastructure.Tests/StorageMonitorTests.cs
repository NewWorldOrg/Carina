using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

public sealed class StorageMonitorTests
{
    private static readonly TimeSpan Rest = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task TheFirstPrecheckReachesTheDriver()
    {
        ScriptedDriverClient client = Answering();
        StorageMonitor monitor = Watching(client, new WoundClock(DateTimeOffset.UnixEpoch));

        DriverCall<IReadOnlyList<StorageRootDto>> answer = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(1, client.StorageReads);
        Assert.True(answer.TryGetValue(out IReadOnlyList<StorageRootDto>? roots));
        Assert.Equal("recorded", Assert.Single(roots).Name);
    }

    [Fact]
    public async Task DecidingWhetherARootTakesAFileIsNotAskedForTwiceWithinTheRest()
    {
        Assert.Equal(1, await ReadsWithAGapOf(Rest - TimeSpan.FromTicks(1)));
        Assert.Equal(2, await ReadsWithAGapOf(Rest));
        Assert.Equal(2, await ReadsWithAGapOf(Rest + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public async Task TheAnswerHandedOutWhileRestingIsTheOneThatWasHeld()
    {
        ScriptedDriverClient client = Answering();
        StorageMonitor monitor = Watching(client, new WoundClock(DateTimeOffset.UnixEpoch));

        DriverCall<IReadOnlyList<StorageRootDto>> first = await monitor.ReadAsync(CancellationToken.None);
        DriverCall<IReadOnlyList<StorageRootDto>> second = await monitor.ReadAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task ADriverThatCouldNotBeReachedIsRestedForTooRatherThanRetriedAtOnce()
    {
        var client = new ScriptedDriverClient
        {
            StorageAnswer = DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable("The socket was not there."),
        };

        StorageMonitor monitor = Watching(client, new WoundClock(DateTimeOffset.UnixEpoch));

        await monitor.ReadAsync(CancellationToken.None);
        DriverCall<IReadOnlyList<StorageRootDto>> second = await monitor.ReadAsync(CancellationToken.None);

        Assert.Equal(1, client.StorageReads);
        Assert.Equal(DriverCallOutcome.Unreachable, second.Outcome);
    }

    [Fact]
    public async Task OnceTheRestIsOverItIsTheNewAnswerThatIsHandedOut()
    {
        ScriptedDriverClient client = Answering();
        var clock = new WoundClock(DateTimeOffset.UnixEpoch);
        StorageMonitor monitor = Watching(client, clock);

        await monitor.ReadAsync(CancellationToken.None);
        client.StorageAnswer = DriverCall<IReadOnlyList<StorageRootDto>>.Reached(
            [new StorageRootDto { Name = "archive", FreeBytes = 5, TotalBytes = 9, Writable = true }]);
        clock.Wind(Rest);

        DriverCall<IReadOnlyList<StorageRootDto>> second = await monitor.ReadAsync(CancellationToken.None);

        Assert.True(second.TryGetValue(out IReadOnlyList<StorageRootDto>? roots));
        Assert.Equal("archive", Assert.Single(roots).Name);
    }

    [Fact]
    public void ARestOfNoTimeAtAllIsNotAPolicy()
    {
        Assert.Equal(
            "restBetweenReads",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StorageMonitorSettings(TimeSpan.Zero)).ParamName);

        Assert.Equal(
            "restBetweenReads",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StorageMonitorSettings(TimeSpan.FromTicks(-1))).ParamName);

        Assert.Equal(
            TimeSpan.FromTicks(1),
            new StorageMonitorSettings(TimeSpan.FromTicks(1)).RestBetweenReads);
    }

    [Fact]
    public void TheRestKeptByDefaultIsAMinute()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), StorageMonitorSettings.Default.RestBetweenReads);
    }

    private static async Task<int> ReadsWithAGapOf(TimeSpan gap)
    {
        ScriptedDriverClient client = Answering();
        var clock = new WoundClock(DateTimeOffset.UnixEpoch);
        StorageMonitor monitor = Watching(client, clock);

        await monitor.ReadAsync(CancellationToken.None);
        clock.Wind(gap);
        await monitor.ReadAsync(CancellationToken.None);

        return client.StorageReads;
    }

    private static StorageMonitor Watching(ScriptedDriverClient client, WoundClock clock)
        => new(client, clock, new StorageMonitorSettings(Rest));

    private static ScriptedDriverClient Answering()
        => new()
        {
            StorageAnswer = DriverCall<IReadOnlyList<StorageRootDto>>.Reached(
                [new StorageRootDto { Name = "recorded", FreeBytes = 1, TotalBytes = 2, Writable = true }]),
        };
}
