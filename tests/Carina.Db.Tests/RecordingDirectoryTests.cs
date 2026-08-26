using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingDirectoryTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ThePageIsBoundedByWhatWasAskedForWhileTheTotalCountsThemAll()
    {
        int network = await StockedAsync(5);

        PaginatedList<Recording> found = await ListAsync(Query(network, perPage: 2));

        Assert.Equal(5, found.Total);
        Assert.Equal(2, found.Items.Count);
        Assert.Equal(3, found.LastPage);
    }

    [Fact]
    public async Task ThePagesAfterTheFirstCarryOnWhereTheOneBeforeStopped()
    {
        int network = await StockedAsync(5);

        PaginatedList<Recording> first = await ListAsync(Query(network, perPage: 2));
        PaginatedList<Recording> second = await ListAsync(Query(network, perPage: 2, page: 2));

        Assert.Empty(first.Items.Select(recording => recording.Id).Intersect(
            second.Items.Select(recording => recording.Id)));
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenIsToldApartFromOneThatHasEnded()
    {
        int network = await StockedAsync(0);
        Recording writing = await AddAsync(network, 1);
        Recording ended = await AddAsync(network, 2, outcome: RecordingOutcome.Truncated);

        PaginatedList<Recording> inFlight = await ListAsync(
            Query(network, standing: RecordingStanding.InFlight));
        PaginatedList<Recording> settled = await ListAsync(Query(network, standing: RecordingStanding.Ended));

        Assert.Equal(writing.Id, Assert.Single(inFlight.Items).Id);
        Assert.Equal(ended.Id, Assert.Single(settled.Items).Id);
    }

    [Fact]
    public async Task AnOutcomeFilterAnswersOnlyTheRecordingsThatEndedThatWay()
    {
        int network = await StockedAsync(0);
        await AddAsync(network, 1, outcome: RecordingOutcome.Complete);
        Recording truncated = await AddAsync(network, 2, outcome: RecordingOutcome.Truncated);
        Recording failed = await AddAsync(network, 3, outcome: RecordingOutcome.Failed);
        await AddAsync(network, 4);

        PaginatedList<Recording> found = await ListAsync(
            Query(network, outcomes: [RecordingOutcome.Truncated, RecordingOutcome.Failed]));

        Assert.Equal(2, found.Total);
        Assert.Contains(truncated.Id, found.Items.Select(recording => recording.Id));
        Assert.Contains(failed.Id, found.Items.Select(recording => recording.Id));
    }

    [Fact]
    public async Task NothingCountedIsNeverAnsweredAsCountedAndClean()
    {
        int network = await StockedAsync(0);
        Recording lost = await AddAsync(network, 1, counters: DropCounters.Counted(3, 1000));
        Recording clean = await AddAsync(network, 2, counters: DropCounters.Counted(0, 1000));
        Recording uncounted = await AddAsync(network, 3);

        PaginatedList<Recording> dropped = await ListAsync(Query(network, drops: DropReading.Dropped));
        PaginatedList<Recording> spotless = await ListAsync(Query(network, drops: DropReading.Clean));
        PaginatedList<Recording> unmeasured = await ListAsync(Query(network, drops: DropReading.Unmeasured));

        Assert.Equal(lost.Id, Assert.Single(dropped.Items).Id);
        Assert.Equal(clean.Id, Assert.Single(spotless.Items).Id);
        Assert.Equal(uncounted.Id, Assert.Single(unmeasured.Items).Id);
    }

    [Fact]
    public async Task AChannelFilterAnswersOnlyTheRecordingsOfTheChannelsItNames()
    {
        int network = await StockedAsync(0);
        Recording first = await AddAsync(network, 1, serviceId: 1024);
        Recording second = await AddAsync(network, 2, serviceId: 1025);
        await AddAsync(network, 3, serviceId: 1026);

        PaginatedList<Recording> found = await ListAsync(Query(
            network,
            channels: [new ProgrammeService(network, 1024), new ProgrammeService(network, 1025)]));

        Assert.Equal(2, found.Total);
        Assert.Contains(first.Id, found.Items.Select(recording => recording.Id));
        Assert.Contains(second.Id, found.Items.Select(recording => recording.Id));
    }

    [Fact]
    public async Task ASpanTakesInWhereItStartsAndStopsShortOfWhereItEnds()
    {
        int network = await StockedAsync(0);
        Recording before = await AddAsync(network, 1, startedAt: Noon.AddMinutes(-1));
        Recording atTheStart = await AddAsync(network, 2, startedAt: Noon);
        Recording atTheEnd = await AddAsync(network, 3, startedAt: Noon.AddHours(1));

        PaginatedList<Recording> found = await ListAsync(Query(network, from: Noon, to: Noon.AddHours(1)));

        Assert.Equal(atTheStart.Id, Assert.Single(found.Items).Id);
        Assert.DoesNotContain(before.Id, found.Items.Select(recording => recording.Id));
        Assert.DoesNotContain(atTheEnd.Id, found.Items.Select(recording => recording.Id));
    }

    [Fact]
    public async Task ASortIsAnsweredInTheDirectionItWasAskedFor()
    {
        int network = await StockedAsync(0);
        Recording first = await AddAsync(network, 1, startedAt: Noon);
        Recording second = await AddAsync(network, 2, startedAt: Noon.AddMinutes(10));

        PaginatedList<Recording> up = await ListAsync(Query(network));
        PaginatedList<Recording> down = await ListAsync(Query(network, descending: true));

        Assert.Equal([first.Id, second.Id], up.Items.Select(recording => recording.Id).ToArray());
        Assert.Equal([second.Id, first.Id], down.Items.Select(recording => recording.Id).ToArray());
    }

    [Fact]
    public async Task TheProgrammeSortIsNotTheSameOrderAsTheOneTheRecordingWasWrittenIn()
    {
        int network = await StockedAsync(0);
        Recording late = await AddAsync(network, 1, startedAt: Noon, programmeStartsAt: Noon.AddHours(2));
        Recording early = await AddAsync(
            network,
            2,
            startedAt: Noon.AddMinutes(10),
            programmeStartsAt: Noon.AddHours(1));

        PaginatedList<Recording> found = await ListAsync(Query(network, sort: RecordingSort.ProgrammeStartsAt));

        Assert.Equal([early.Id, late.Id], found.Items.Select(recording => recording.Id).ToArray());
    }

    [Fact]
    public async Task ARecordingIsFoundByTheNameTheLedgerHolds()
    {
        int network = await StockedAsync(0);
        Recording recording = await AddAsync(network, 1);

        await using CarinaDbContext context = Context();
        Recording? found = await new RecordingDirectory(context).FindAsync(recording.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(recording.Id, found.Id);
        Assert.Null(await new RecordingDirectory(context).FindAsync(RecordingId.New(), CancellationToken.None));
    }

    [Fact]
    public async Task AStopByHandLeavesTheReasonOnTheRowAndSaysThisSideAskedForIt()
    {
        int network = await StockedAsync(0);
        Recording recording = await AddAsync(network, 1);

        RecordingHalt halt;

        await using (CarinaDbContext writing = Context())
        {
            halt = await new RecordingDirectory(writing).HaltAsync(
                recording.Id,
                new RecordingStopReason("the wrong programme"),
                Noon.AddMinutes(30),
                CancellationToken.None);
        }

        await using CarinaDbContext reading = Context();
        Recording read = await reading.Set<Recording>().SingleAsync(held => held.Id == recording.Id);
        OutcomeDetail kept = Assert.Single(read.OutcomeDetail);

        Assert.Equal(RecordingHalt.Written, halt);
        Assert.Equal(RecordingFault.StoppedByHand, kept.Fault);
        Assert.Equal("the wrong programme", kept.Note);
        Assert.Equal(Noon.AddMinutes(30), read.AbortedAt);
        Assert.Null(read.Outcome);
    }

    [Fact]
    public async Task AStopIsRefusedForARecordingThatHasEndedAndForOneNobodyHas()
    {
        int network = await StockedAsync(0);
        Recording ended = await AddAsync(network, 1, outcome: RecordingOutcome.Complete);

        await using CarinaDbContext context = Context();
        var directory = new RecordingDirectory(context);
        var reason = new RecordingStopReason("too late");

        Assert.Equal(
            RecordingHalt.AlreadyEnded,
            await directory.HaltAsync(ended.Id, reason, Noon.AddHours(2), CancellationToken.None));
        Assert.Equal(
            RecordingHalt.NoSuchRecording,
            await directory.HaltAsync(RecordingId.New(), reason, Noon, CancellationToken.None));
    }

    private static RecordingQuery Query(
        int network,
        RecordingStanding? standing = null,
        IReadOnlyList<RecordingOutcome>? outcomes = null,
        DropReading? drops = null,
        IReadOnlyList<ProgrammeService>? channels = null,
        DateTime? from = null,
        DateTime? to = null,
        RecordingSort sort = RecordingSort.StartedAt,
        bool descending = false,
        int? page = null,
        int? perPage = null)
    {
        RecordingQuery? query = RecordingQuery.For(
            from,
            to,
            sort,
            descending,
            page,
            perPage,
            new RecordingConditions
            {
                Standing = standing,
                Outcomes = outcomes,
                Drops = drops,
                Channels = channels ?? [.. Enumerable.Range(1024, 8).Select(service =>
                    new ProgrammeService(network, service))],
            });

        return query ?? throw new InvalidOperationException("The query this test asks for is one the guard takes.");
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<PaginatedList<Recording>> ListAsync(RecordingQuery query)
    {
        await using CarinaDbContext context = Context();

        return await new RecordingDirectory(context).ListAsync(query, CancellationToken.None);
    }

    private async Task<int> StockedAsync(int recordings)
    {
        int network = Interlocked.Increment(ref networks) + 40_000;

        foreach (int eventId in Enumerable.Range(1, recordings))
        {
            await AddAsync(network, eventId);
        }

        return network;
    }

    private static int networks;

    private async Task<Recording> AddAsync(
        int network,
        int eventId,
        int serviceId = 1024,
        DateTime? startedAt = null,
        DateTime? programmeStartsAt = null,
        RecordingOutcome? outcome = null,
        DropCounters? counters = null)
    {
        RecordingId id = RecordingId.New();
        DateTime started = startedAt ?? Noon.AddMinutes(eventId);

        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(
                new NetworkId(network),
                new ServiceId(serviceId),
                new EventId(eventId),
                programmeStartsAt ?? started),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            started,
            started.AddHours(1),
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], started),
            null,
            BroadcastGroupRole.Standalone,
            started,
            new TunerDeviceId("pt3-0"));

        if (counters is { } counted)
        {
            recording.Measure(counted, DropTimeline.Unlocated, null, 0, started.AddMinutes(1));
        }

        if (outcome is { } settled)
        {
            recording.Wrote(TimeSpan.FromHours(1));

            if (settled is not RecordingOutcome.Complete)
            {
                recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, started));
            }

            recording.Abort(started.AddHours(1));
            recording.Settle(settled, settled is RecordingOutcome.Failed ? 0 : 1_000, started.AddHours(1));
        }

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
