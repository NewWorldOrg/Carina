using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CollectionRoundTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static int nextNetworkId = 40000;

    [Fact]
    public async Task EveryStreamIsVisitedAndWhatItGaveIsWrittenDown()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));
        driver.Script(TuningParameters.Terrestrial(24), Carrying(network, 2));

        await using CarinaDbContext context = database.Open();
        CollectionRound round = Round(driver, context);

        RoundResult walked = await round.WalkAsync(
            [Stream(network, 1, 22), Stream(network, 2, 24)],
            Cancel, Cancel);

        Assert.Equal(new RoundResult(2, 2, 0), walked);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<StreamVisit> visits = await new StreamVisitRepository(reading).ListAsync(Cancel);

        Assert.Equal(2, visits.Count(visit => visit.NetworkId.Value == network));
        Assert.All(
            visits.Where(visit => visit.NetworkId.Value == network),
            visit => Assert.Equal(VisitOutcome.BasicOnly, visit.Outcome));
    }

    [Fact]
    public async Task AStreamThatCameBackShortIsCountedAndItsLedgerSaysSo()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using CarinaDbContext context = database.Open();

        RoundResult walked = await Round(driver, context).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        Assert.Equal(new RoundResult(1, 0, 1), walked);

        await using CarinaDbContext reading = database.Open();
        StreamVisit? visit = await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel);

        Assert.Equal(VisitOutcome.NoLock, visit!.Outcome);
        Assert.Equal(1, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public async Task AStreamStillBackingOffIsNotVisitedAgainYet()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using CarinaDbContext context = database.Open();
        CollectionRound round = Round(driver, context);

        await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        Assert.Equal(new RoundResult(0, 0, 0), await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel));
    }

    [Fact]
    public async Task ComingBackShortTwiceAddsUpInTheLedger()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using CarinaDbContext context = database.Open();
        CollectionRound round = Round(driver, context, new CollectionSettings { BeforeRetrying = TimeSpan.Zero });

        await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);
        await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        await using CarinaDbContext reading = database.Open();
        StreamVisit? visit = await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel);

        Assert.Equal(2, visit!.ConsecutiveIncomplete);
    }

    [Fact]
    public async Task OneStreamFailingOutrightDoesNotStopTheOnesBehindIt()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient { UnreachableFrom = "adapter0" };

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());
        driver.Script(TuningParameters.Terrestrial(24), Carrying(network, 2));

        await using CarinaDbContext context = database.Open();

        RoundResult walked = await Round(driver, context).WalkAsync(
            [Stream(network, 1, 22), Stream(network, 2, 24)],
            Cancel, Cancel);

        Assert.Equal(2, walked.Visited);
    }

    [Fact]
    public async Task NothingToVisitWalksNowhere()
    {
        await using CarinaDbContext context = database.Open();

        Assert.Equal(
            new RoundResult(0, 0, 0),
            await Round(new ScriptedDriverClient(), context).WalkAsync([], Cancel, Cancel));
    }

    [Fact]
    public async Task ABusyTunerIsWaitedOutRatherThanCountedAgainstTheStream()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient { BusyRefusalsRemaining = 2 };
        var clock = new HurriedClock();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        RoundResult walked = await Round(driver, context, clock: clock)
            .WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        Assert.Equal(new RoundResult(1, 1, 0), walked);
        Assert.Equal(2, clock.Waits.Count);
    }

    [Fact]
    public async Task AWalkStepsBackWhenEveryTunerStaysBusyInsteadOfBurningThroughThePlan()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient { BusyRefusalsRemaining = 100 };

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));
        driver.Script(TuningParameters.Terrestrial(24), Carrying(network, 2));

        await using CarinaDbContext context = database.Open();

        RoundResult walked = await Round(driver, context, clock: new HurriedClock())
            .WalkAsync([Stream(network, 1, 22), Stream(network, 2, 24)], Cancel, Cancel);

        Assert.Equal(new RoundResult(0, 0, 0), walked);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<StreamVisit> visits = await new StreamVisitRepository(reading).ListAsync(Cancel);
        StreamVisit recorded = Assert.Single(visits, visit => visit.NetworkId.Value == network);

        Assert.Equal(VisitOutcome.Interrupted, recorded.Outcome);
        Assert.Equal(0, recorded.ConsecutiveIncomplete);
    }

    private static CollectionRound Round(
        ScriptedDriverClient driver,
        CarinaDbContext context,
        CollectionSettings? settings = null,
        TimeProvider? clock = null)
    {
        var programmes = new ProgrammeRepository(context);
        CollectionSettings carried = settings ?? new CollectionSettings();

        return new CollectionRound(
            new StreamVisitRepository(context),
            programmes,
            new StreamVisitor(
                driver,
                new ProgrammeWriter(programmes, new UnguardedWrites(), new StillClock()),
                carried),
            carried,
            clock ?? TimeProvider.System,
            NullLogger<CollectionRound>.Instance);
    }

    private static BroadcastStream Stream(int network, int stream, int channel)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(channel),
            [new ServiceId(1049)]);

    private static ChannelScript Carrying(int network, int stream)
        => new() { Bytes = Schedule(network, stream) };

    private static byte[] Schedule(int network, int stream)
        => [.. new TransportStreamWriter(EventInformationTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = EventInformationTable.FirstScheduleActualTableId,
                TableIdExtension = 1049,
                LastSectionNumber = 0,
                Body =
                [
                    (byte)(stream >> 8), (byte)(stream & 0xFF),
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, EventInformationTable.FirstScheduleActualTableId,
                    0x00, 0x01,
                    0xEF, 0x55, 0x22, 0x57, 0x00,
                    0x00, 0x03, 0x00,
                    0x00, 0x00,
                ],
            }.ToBytes())
            .Packets
            .SelectMany(packet => packet.ToArray())];

    private static int NextNetwork() => 40000 + (Interlocked.Increment(ref nextNetworkId) % 20000);
}
