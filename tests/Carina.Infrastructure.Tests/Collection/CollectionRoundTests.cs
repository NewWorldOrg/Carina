using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Contracts;
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
    public async Task AHurriedWalkVisitsAStreamThatIsStillBackingOff()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using CarinaDbContext context = database.Open();
        CollectionRound round = Round(driver, context);

        await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        Assert.Equal(
            new RoundResult(1, 0, 1),
            await round.WalkAsync([Stream(network, 1, 22)], Cancel, Cancel, hurried: true));
    }

    [Fact]
    public async Task AHurriedWalkAsksTheDriverForTheHurriedPurpose()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        await Round(driver, context).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel, hurried: true);

        Assert.Equal([SessionPurpose.SurveyNow], driver.Purposes);
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

    [Fact]
    public async Task AStreamDeclaringAServiceTheCatalogueDoesNotHoldSuggestsARescan()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var events = new SilentEvents();
        var board = new RescanNoticeBoard(events, TimeProvider.System);

        driver.Script(
            TuningParameters.Terrestrial(22),
            new ChannelScript { Bytes = [.. Schedule(network, 1), .. Described(network, 1, [1049, 1050])] });

        await using CarinaDbContext context = database.Open();

        await Round(driver, context, board: board).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        RescanNotice only = Assert.Single(board.Standing);

        Assert.Equal(RescanReason.ServicesAppeared, only.Hint.Reason);
        Assert.Equal([1050], only.Hint.Services.Select(service => service.Value));
        Assert.Equal([AppEventName.Tuners], events.Signalled);
    }

    [Fact]
    public async Task AStreamDeclaringExactlyWhatWeHoldSuggestsNothing()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var events = new SilentEvents();
        var board = new RescanNoticeBoard(events, TimeProvider.System);

        driver.Script(
            TuningParameters.Terrestrial(22),
            new ChannelScript { Bytes = [.. Schedule(network, 1), .. Described(network, 1, [1049])] });

        await using CarinaDbContext context = database.Open();

        await Round(driver, context, board: board).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        Assert.Empty(board.Standing);
        Assert.Empty(events.Signalled);
    }

    [Fact]
    public async Task AChannelThatWouldNotLockIsReportedToTheTuner()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var reports = new RememberedTuneReports();
        var candidateChannelId = CandidateChannelId.New();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using CarinaDbContext context = database.Open();

        await Round(driver, context, reports: reports)
            .WalkAsync([TunedWith(Stream(network, 1, 22), candidateChannelId)], Cancel, Cancel);

        Assert.Equal([candidateChannelId], reports.Failures);
        Assert.Empty(reports.Reached);
    }

    [Fact]
    public async Task AStreamThatLockedButNeverFinishedItsGuideIsNotReportedAsAFailure()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var reports = new RememberedTuneReports();
        var candidateChannelId = CandidateChannelId.New();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        await Round(driver, context, reports: reports)
            .WalkAsync([TunedWith(Stream(network, 1, 22), candidateChannelId)], Cancel, Cancel);

        Assert.Empty(reports.Failures);
        Assert.Equal([candidateChannelId], reports.Reached);
    }

    [Fact]
    public async Task AVisitTheDriverCutShortSaysNothingAboutTheChannelEitherWay()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient { BusyRefusalsRemaining = 100 };
        var reports = new RememberedTuneReports();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        await Round(driver, context, clock: new HurriedClock(), reports: reports)
            .WalkAsync([TunedWith(Stream(network, 1, 22), CandidateChannelId.New())], Cancel, Cancel);

        Assert.Empty(reports.Failures);
        Assert.Empty(reports.Reached);
    }

    private static BroadcastStream TunedWith(BroadcastStream stream, CandidateChannelId candidateChannelId)
        => stream with { TunedWith = candidateChannelId };

    private static byte[] Described(int network, int stream, IReadOnlyList<int> services)
        => [.. new TransportStreamWriter(ServiceDescriptionTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = ServiceDescriptionTable.ActualStreamTableId,
                TableIdExtension = stream,
                LastSectionNumber = 0,
                Body = new SdtWriter
                {
                    OriginalNetworkId = network,
                    Services = [.. services.Select(service => SdtWriter.Service(
                        service,
                        SiDescriptorWriter.Service(
                            (int)ServiceKind.Television,
                            [],
                            new AribTextWriter().Kanji("試験").ToArray())))],
                }.ToBody(),
            }.ToBytes())
            .Packets
            .SelectMany(packet => packet.ToArray())];

    private static CollectionRound Round(
        ScriptedDriverClient driver,
        CarinaDbContext context,
        CollectionSettings? settings = null,
        TimeProvider? clock = null,
        RescanNoticeBoard? board = null,
        RememberedTuneReports? reports = null)
    {
        var programmes = new ProgrammeRepository(context);
        CollectionSettings carried = settings ?? new CollectionSettings();

        return new CollectionRound(
            new StreamVisitRepository(context),
            programmes,
            new StreamVisitor(
                driver,
                new ProgrammeWriter(programmes, new UnguardedWrites(), new StillClock(), new SilentEvents()),
                carried,
                clock ?? TimeProvider.System),
            board ?? new RescanNoticeBoard(new SilentEvents(), TimeProvider.System),
            reports ?? new RememberedTuneReports(),
            new SilentEvents(),
            carried,
            clock ?? TimeProvider.System,
            NullLogger<CollectionRound>.Instance);
    }

    [Fact]
    public async Task WhatEachTableDeclaredAgainstWhatArrivedIsKeptWithTheVisit()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        await Round(driver, context).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        await using CarinaDbContext reading = database.Open();
        StreamVisit visit = (await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel))!;
        VisitTally counted = Assert.Single(visit.Tally);

        Assert.Equal(1049, counted.ServiceId.Value);
        Assert.Equal(EventInformationTable.FirstScheduleActualTableId, counted.TableId);
        Assert.Equal(EventInformationTable.FirstScheduleActualTableId, counted.LastTableId);
        Assert.Equal(1, counted.SegmentsDeclared);
        Assert.Equal(1, counted.SegmentsHeard);
        Assert.Equal(1, counted.SectionsDeclared);
        Assert.Equal(1, counted.SectionsHeard);
        Assert.Equal(0, counted.VersionChanges);
    }

    [Fact]
    public async Task TheTallyOfASecondVisitReplacesTheFirstWithoutLosingTheLedgerRow()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));

        await using CarinaDbContext context = database.Open();

        await Round(driver, context).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        await using CarinaDbContext again = database.Open();

        await Round(driver, again).WalkAsync([Stream(network, 1, 22)], Cancel, Cancel);

        await using CarinaDbContext reading = database.Open();
        StreamVisit visit = (await new StreamVisitRepository(reading).FindAsync(
            new NetworkId(network),
            new TransportStreamId(1),
            Cancel))!;

        Assert.Single(visit.Tally);
    }

    [Fact]
    public async Task HowFarAheadWeWantToSeeIsNotWhatMakesAStreamWorthGoingBackTo()
    {
        int network = NextNetwork();
        DateTime now = DateTime.UtcNow;
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), Carrying(network, 1));
        driver.Script(TuningParameters.Terrestrial(24), Carrying(network, 2));

        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);
        var visits = new StreamVisitRepository(context);

        await programmes.AddAsync(Reaching(network, 1049, now.AddDays(4)), Cancel);
        await programmes.AddAsync(Reaching(network, 1050, now.AddDays(6)), Cancel);
        await visits.SaveAsync(Settled(network, 1, now.AddHours(-1)), Cancel);
        await visits.SaveAsync(Settled(network, 2, now.AddHours(-48)), Cancel);

        await Round(driver, context, Aiming(TimeSpan.FromDays(8), TimeSpan.FromDays(3))).WalkAsync(
            [Serving(network, 1, 22, 1049), Serving(network, 2, 24, 1050)],
            Cancel, Cancel);

        Assert.Equal(
            [TuningParameters.Terrestrial(24), TuningParameters.Terrestrial(22)],
            driver.Started);
    }

    private static CollectionSettings Aiming(TimeSpan wanted, TimeSpan revisitsBelow)
        => new()
        {
            WantedCoverage = wanted,
            RevisitsBelow = revisitsBelow,
            BetweenVisits = TimeSpan.Zero,
        };

    private static Programme Reaching(int network, int service, DateTime until)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(1)),
                new TransportStreamId(1),
                until.AddMinutes(-30),
                until,
                "報道",
                string.Empty,
                false),
            until.AddMinutes(-30));

    private static StreamVisit Settled(int network, int stream, DateTime at)
        => StreamVisit.Record(
            new NetworkId(network),
            new TransportStreamId(stream),
            VisitOutcome.BasicOnly,
            at,
            TimeSpan.FromSeconds(1));

    private static BroadcastStream Serving(int network, int stream, int channel, int service)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(channel),
            [new ServiceId(service)]);

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

    private static int NextNetwork() => BroadcastIds.NextNetwork();
}
