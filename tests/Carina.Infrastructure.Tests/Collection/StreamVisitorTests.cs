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
public sealed class StreamVisitorTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TuningParameters Channel = TuningParameters.Terrestrial(22);

    private static int nextNetworkId = 60000;

    [Fact]
    public async Task AVisitGathersTheTablesAndWritesTheProgrammesTheyCarry()
    {
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(network) });

        await using var context = database.Open();
        var result = await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.BasicOnly, result.Outcome);
        Assert.Equal(1, result.Written.Added);

        await using var reading = database.Open();

        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel));
    }

    [Fact]
    public async Task AChannelThatDoesNotLockIsNamedAsATuningFailure()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.NoLock());

        await using var context = database.Open();
        var result = await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.NoLock, result.Outcome);
        Assert.Equal(new ProgrammesWritten(0, 0, 0), result.Written);
    }

    [Fact]
    public async Task TheSessionIsLetGoWhicheverWayTheVisitEnds()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using var context = database.Open();

        await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task AHurriedVisitAsksForTheHurriedPurpose()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using var context = database.Open();

        await Visitor(driver, context).VisitAsync(Channel, hurried: true, Cancel);

        Assert.Contains(Contracts.SessionPurpose.SurveyNow, driver.Purposes);
    }

    [Fact]
    public async Task AnOrdinaryVisitAsksForTheOrdinaryPurpose()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using var context = database.Open();

        await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal([Contracts.SessionPurpose.Survey], driver.Purposes);
    }

    [Fact]
    public async Task AChannelThatLocksButStaysSilentIsNamedAsCarryingNoBytes()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Silent());

        await using var context = database.Open();
        var result = await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.NoBytes, result.Outcome);
    }

    [Fact]
    public async Task TheSessionIsLetGoEvenWhenTheStreamTears()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Paced = PacedStream.Torn });

        await using var context = database.Open();
        var result = await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.Interrupted, result.Outcome);
        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task PacketsArrivingInOddSizedChunksAreStillRead()
    {
        var network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var packets = Schedule(network);

        driver.Script(Channel, new ChannelScript { Paced = () => PacedStream.Sliced(packets, 100) });

        await using var context = database.Open();
        var result = await Visitor(driver, context).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.BasicOnly, result.Outcome);
        Assert.Equal(0, result.UnreadablePackets);
    }

    private static StreamVisitor Visitor(ScriptedDriverClient driver, CarinaDbContext context)
        => new(
            driver,
            new ProgrammeWriter(new ProgrammeRepository(context), new UnguardedWrites(), new StillClock()),
            new CollectionSettings());

    private static byte[] Schedule(int network)
    {
        byte[] section(int tableId, int lastTableId) => new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = 1049,
            LastSectionNumber = 0,
            Body =
            [
                0x7F, 0xE3,
                (byte)(network >> 8), (byte)(network & 0xFF),
                0x00, (byte)lastTableId,
                0x00, 0x01,
                0xEF, 0x55, 0x22, 0x57, 0x00,
                0x00, 0x03, 0x00,
                0x00, 0x00,
            ],
        }.ToBytes();

        return [.. new TransportStreamWriter(EventInformationTable.Pid)
            .Sections(section(0x50, 0x50))
            .Packets
            .SelectMany(packet => packet.ToArray())];
    }

    private static int NextNetwork() => Interlocked.Increment(ref nextNetworkId);
}
