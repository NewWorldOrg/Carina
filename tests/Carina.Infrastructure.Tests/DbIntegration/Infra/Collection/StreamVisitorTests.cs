using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Tests.Fixtures.Collection;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class StreamVisitorTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TuningParameters Channel = TuningParameters.Terrestrial(22);

    [Fact]
    public async Task AVisitGathersTheTablesAndWritesTheProgrammesTheyCarry()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(network) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.BasicOnly, result.Outcome);
        Assert.Equal(1, result.Written.Added);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.NotNull(await reading.ServiceProvider.GetRequiredService<IProgrammeRepository>().FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel));
    }

    [Fact]
    public async Task ATunerThatWasBusyLeavesTheStreamUnblamedAndWorthWaitingFor()
    {
        var driver = new ScriptedDriverClient { BusyRefusalsRemaining = 1 };

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.Interrupted, result.Outcome);
        Assert.True(result.WorthWaitingOut);
    }

    [Fact]
    public async Task ARefusalThatNamesTheLockIsStillATuningFailure()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(
            Channel,
            new ChannelScript { Refusal = new DriverProblem(SessionRefusalTitles.NoLock, []) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.NoLock, result.Outcome);
        Assert.False(result.WorthWaitingOut);
    }

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, VisitOutcome.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, VisitOutcome.NoBytes)]
    [InlineData(null, VisitOutcome.NoLock)]
    public async Task ASessionHandedBackAlreadyFailedSaysWhichWayReceptionWentWrong(
        string? title,
        VisitOutcome outcome)
    {
        var driver = new ScriptedDriverClient();

        driver.Script(
            Channel,
            new ChannelScript
            {
                State = SessionState.Failed,
                FailureCause = "the tuner could not receive.",
                FailureTitle = title,
            });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(outcome, result.Outcome);
    }

    [Fact]
    public async Task AChannelThatDoesNotLockIsNamedAsATuningFailure()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.NoLock());

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.NoLock, result.Outcome);
        Assert.Equal(new ProgrammesWritten(0, 0, 0), result.Written);
    }

    [Fact]
    public async Task TheSessionIsLetGoWhicheverWayTheVisitEnds()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task AHurriedVisitAsksForTheHurriedPurpose()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Visitor(scope).VisitAsync(Channel, hurried: true, Cancel);

        Assert.Contains(Puppis.Contracts.SessionPurpose.SurveyNow, driver.Purposes);
    }

    [Fact]
    public async Task AnOrdinaryVisitAsksForTheOrdinaryPurpose()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Bytes = Schedule(NextNetwork()) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal([Puppis.Contracts.SessionPurpose.Survey], driver.Purposes);
    }

    [Fact]
    public async Task AChannelThatLocksButStaysSilentIsNamedAsCarryingNoBytes()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Silent());

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.NoBytes, result.Outcome);
    }

    [Fact]
    public async Task TheSessionIsLetGoEvenWhenTheStreamTears()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, new ChannelScript { Paced = PacedStream.Torn });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.Interrupted, result.Outcome);
        Assert.Single(driver.Stopped);
    }

    [Fact]
    public async Task PacketsArrivingInOddSizedChunksAreStillRead()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        byte[] packets = Schedule(network);

        driver.Script(Channel, new ChannelScript { Paced = () => PacedStream.Sliced(packets, 100) });

        await using ServiceProvider container = Container(driver);
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        VisitResult result = await Visitor(scope).VisitAsync(Channel, hurried: false, Cancel);

        Assert.Equal(VisitOutcome.BasicOnly, result.Outcome);
        Assert.Equal(0, result.UnreadablePackets);
    }

    private ServiceProvider Container(ScriptedDriverClient driver)
        => database.Infrastructure(replace =>
        {
            replace.AddSingleton<IDriverClient>(driver);
            replace.AddSingleton(new CollectionSettings());
            CollectionFixtures.WriterOnAStillClock(replace);
        });

    private static StreamVisitor Visitor(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<StreamVisitor>();

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

    private static int NextNetwork() => BroadcastIds.NextNetwork();
}
