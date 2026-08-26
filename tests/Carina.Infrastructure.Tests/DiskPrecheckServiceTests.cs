using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

public sealed class DiskPrecheckServiceTests
{
    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Recorded = new("recorded");

    [Fact]
    public async Task ADriverThatCouldNotBeReachedLeavesTheRootsUnknownRatherThanUndeclared()
    {
        DiskPrecheckVerdict verdict = await Weighing(
            DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable("The socket was not there."));

        Assert.Equal(DiskShortfall.RootsUnknown, verdict.Shortfall);
        Assert.Equal(1, verdict.Weighed);
    }

    [Fact]
    public async Task EveryShortfallComesBackAsAVerdictSoNothingHereCanStopARecording()
    {
        (DiskShortfall Expected, DriverCall<IReadOnlyList<StorageRootDto>> Answer)[] cases =
        [
            (DiskShortfall.RootsUnknown, DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable("no socket")),
            (DiskShortfall.RootUndeclared, Answering()),
            (DiskShortfall.RootUnmeasured, Answering(new StorageRootDto { Name = "recorded" })),
            (DiskShortfall.RootNotWritable, Answering(Room(free: 100_000_000_000L, writable: false))),
            (DiskShortfall.NoRoomLeft, Answering(Room(free: 0))),
            (DiskShortfall.ShortOfTheEstimate, Answering(Room(free: 1))),
        ];

        List<DiskShortfall> named = [];

        foreach ((DiskShortfall expected, DriverCall<IReadOnlyList<StorageRootDto>> answer) in cases)
        {
            DiskPrecheckVerdict verdict = await Weighing(answer);

            Assert.Equal(expected, verdict.Shortfall);
            Assert.False(verdict.HasRoom);
            Assert.Equal(1, verdict.Weighed);

            named.Add(expected);
        }

        Assert.Equal(Enum.GetValues<DiskShortfall>(), named);
    }

    [Fact]
    public async Task ARootWithRoomToSpareIsNoFindingAtAll()
    {
        DiskPrecheckVerdict verdict = await Weighing(Answering(Room(free: 100_000_000_000L)));

        Assert.True(verdict.HasRoom);
        Assert.Null(verdict.Shortfall);
        Assert.Equal((Int128)7_425_000_000L, verdict.EstimatedBytes);
    }

    [Fact]
    public async Task TwoPrechecksInsideTheRestCostTheDriverOneWrite()
    {
        var client = new ScriptedDriverClient { StorageAnswer = Answering(Room(free: 100_000_000_000L)) };
        var service = new DiskPrecheckService(
            new StorageMonitor(client, new WoundClock(DateTimeOffset.UnixEpoch), StorageMonitorSettings.Default));

        await service.WeighAsync(Recorded, Starting(), [], Noon, CancellationToken.None);
        await service.WeighAsync(Recorded, Starting(), [], Noon, CancellationToken.None);

        Assert.Equal(1, client.StorageReads);
    }

    private static async Task<DiskPrecheckVerdict> Weighing(DriverCall<IReadOnlyList<StorageRootDto>> answer)
    {
        var client = new ScriptedDriverClient { StorageAnswer = answer };
        var service = new DiskPrecheckService(
            new StorageMonitor(client, new WoundClock(DateTimeOffset.UnixEpoch), StorageMonitorSettings.Default));

        return await service.WeighAsync(Recorded, Starting(), [], Noon, CancellationToken.None);
    }

    private static RecordingDemand Starting()
        => new(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

    private static DriverCall<IReadOnlyList<StorageRootDto>> Answering(params StorageRootDto[] roots)
        => DriverCall<IReadOnlyList<StorageRootDto>>.Reached(roots);

    private static StorageRootDto Room(long free, bool writable = true)
        => new()
        {
            Name = "recorded",
            FreeBytes = free,
            TotalBytes = 100_000_000_000_000L,
            Writable = writable,
        };
}
