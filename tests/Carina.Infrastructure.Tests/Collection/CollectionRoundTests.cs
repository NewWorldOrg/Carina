using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

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
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));
        driver.Script(TuningParameters.Terrestrial(24), Carrying(network, 2));

        await using var context = database.Open();
        var round = Round(driver, context);

        var walked = await round.WalkAsync(
            [Stream(network, 1, 22), Stream(network, 2, 24)],
            Cancel);

        Assert.Equal(new RoundResult(2, 2, 0), walked);

        await using var reading = database.Open();
        var visits = await new StreamVisitRepository(reading).ListAsync(Cancel);

        Assert.Equal(2, visits.Count(visit => visit.NetworkId.Value == network));
        Assert.All(
            visits.Where(visit => visit.NetworkId.Value == network),
            visit => Assert.Equal(VisitOutcome.BasicOnly, visit.Outcome));
    }

    [Fact]
    public async Task AStreamThatCameBackShortIsCountedAndItsLedgerSaysSo()
    {
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var context = database.Open();

        var walked = await Round(driver, context).WalkAsync([Stream(network, 1, 22)], Cancel);

        Assert.Equal(new RoundResult(1, 0, 1), walked);

        await using var reading = database.Open();
        var visit = await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel);

        Assert.Equal(VisitOutcome.NoLock, visit!.Outcome);
        Assert.Equal(1, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public async Task AStreamStillBackingOffIsNotVisitedAgainYet()
    {
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var context = database.Open();
        var round = Round(driver, context);

        await round.WalkAsync([Stream(network, 1, 22)], Cancel);

        Assert.Equal(new RoundResult(0, 0, 0), await round.WalkAsync([Stream(network, 1, 22)], Cancel));
    }

    [Fact]
    public async Task ComingBackShortTwiceAddsUpInTheLedger()
    {
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var context = database.Open();
        var round = Round(driver, context, new CollectionSettings { BeforeRetrying = TimeSpan.Zero });

        await round.WalkAsync([Stream(network, 1, 22)], Cancel);
        await round.WalkAsync([Stream(network, 1, 22)], Cancel);

        await using var reading = database.Open();
        var visit = await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel);

        Assert.Equal(2, visit!.ConsecutiveIncomplete);
    }

    [Fact]
    public async Task NothingToVisitWalksNowhere()
    {
        await using var context = database.Open();

        Assert.Equal(
            new RoundResult(0, 0, 0),
            await Round(new ScriptedDriverClient(), context).WalkAsync([], Cancel));
    }

    private static CollectionRound Round(
        ScriptedDriverClient driver,
        CarinaDbContext context,
        CollectionSettings? settings = null)
    {
        var programmes = new ProgrammeRepository(context);
        var carried = settings ?? new CollectionSettings();

        return new CollectionRound(
            new StreamVisitRepository(context),
            programmes,
            new StreamVisitor(
                driver,
                new ProgrammeWriter(programmes, new UnguardedWrites(), new StillClock()),
                carried),
            carried,
            TimeProvider.System);
    }

    private static StreamToVisit Stream(int network, int stream, int channel)
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
