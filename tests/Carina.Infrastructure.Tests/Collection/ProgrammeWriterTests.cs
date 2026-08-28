using Carina.Broadcast.Tables;
using Carina.Broadcast.Text;
using Carina.BroadcastTestSupport;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWriterTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = StillClock.Now.UtcDateTime;


    [Fact]
    public async Task ATableTurnsIntoProgrammesTheStoreCanHandBack()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        ProgrammesWritten written = await Writer(context).WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(new ProgrammesWritten(1, 0, 0), written);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        Assert.Equal("あさイチ", stored!.Name);
        Assert.Equal(new DateTime(2026, 8, 17, 13, 57, 0, DateTimeKind.Utc), stored.StartsAt);
        Assert.Equal(TimeSpan.FromMinutes(3), stored.EndsAt - stored.StartsAt);
        Assert.Equal(ProgrammeSource.PresentFollowing, stored.Source);
    }

    [Fact]
    public async Task TheSameTableArrivingAgainWritesNothingNew()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(new ProgrammesWritten(0, 0, 0), await writer.WriteAsync([Table(network, 1)], Cancel));
    }

    [Fact]
    public async Task ATableThatSaysSomethingNewUpdatesWhatWasThere()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal(
            new ProgrammesWritten(0, 1, 0),
            await writer.WriteAsync([Table(network, 1, name: "ひるまえほっと")], Cancel));

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(
            "ひるまえほっと",
            (await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel))!.Name);
    }

    [Fact]
    public async Task OneEventSeenInSeveralTablesBecomesOneProgrammeCarryingAllOfIt()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        ProgrammesWritten written = await Writer(context).WriteAsync(
            [Table(network, 1), DetailTable(network, 1)],
            Cancel);

        Assert.Equal(new ProgrammesWritten(1, 0, 0), written);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        Assert.Equal("あさイチ", stored!.Name);
        Assert.Equal("Heading", Assert.Single(stored.Items).Heading);
    }

    [Fact]
    public async Task TheSameVisitArrivingAgainWritesNothingEvenAcrossSeveralTables()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);
        EventInformationTable[] visit = [Table(network, 1), DetailTable(network, 1)];

        await writer.WriteAsync(visit, Cancel);

        Assert.Equal(new ProgrammesWritten(0, 0, 0), await writer.WriteAsync(visit, Cancel));
    }

    [Fact]
    public async Task AProgrammeNamesTheOthersItIsSharedWithButNeverItself()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        byte[] group =
        [
            0xD6, 0x09,
            0x12,
            0x04, 0x19, 0x00, 0x01,
            0x04, 0x18, 0x00, 0x01,
        ];

        await Writer(context).WriteAsync([Table(network, 1, extra: group)], Cancel);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        RelatedProgramme related = Assert.Single(stored!.Related);

        Assert.Equal(network, related.NetworkId);
        Assert.Equal(1048, related.ServiceId);
        Assert.Equal(1, related.EventId);
        Assert.Equal(RelationKind.Shared, related.Kind);
    }

    [Fact]
    public async Task EventsTheTableItselfThrewAwayAreCountedHereToo()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        ProgrammesWritten written = await Writer(context).WriteAsync([Table(network, 1, unreadableStart: true)], Cancel);

        Assert.Equal(new ProgrammesWritten(0, 0, 1), written);
    }

    [Fact]
    public async Task AWriteThatStoredAProgrammeAsksForTheRulesToBeReadAgainstItAgain()
    {
        int network = NextNetwork();
        var notices = new CountedNotices();
        await using CarinaDbContext context = database.Open();

        await Writer(context, notices).WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal([RecalculationTrigger.ProgrammesChanged], notices.Nudged);
    }

    [Fact]
    public async Task AWriteThatStoredNothingAsksForNothingToBeReadAgain()
    {
        int network = NextNetwork();
        var notices = new CountedNotices();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context, notices);

        await writer.WriteAsync([Table(network, 1)], Cancel);
        notices.Nudged.Clear();

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Empty(notices.Nudged);
    }

    [Fact]
    public async Task AWriteThatOnlyThrewAwayWhatItCouldNotReadTellsTheScreensAndAsksForNoRulesToBeRead()
    {
        int network = NextNetwork();
        var notices = new CountedNotices();
        var events = new SilentEvents();
        await using CarinaDbContext context = database.Open();

        ProgrammesWritten written = await Writer(context, notices, events)
            .WriteAsync([Table(network, 1, unreadableStart: true)], Cancel);

        Assert.Equal(0, written.Added);
        Assert.Equal(0, written.Updated);
        Assert.True(written.Discarded > 0);
        Assert.Equal([AppEventName.Programs], events.Signalled);
        Assert.Empty(notices.Nudged);
    }

    private static ProgrammeWriter Writer(
        CarinaDbContext context,
        IRecalculationNotice? notices = null,
        SilentEvents? events = null)
        => new(
            new ProgrammeRepository(context),
            new UnguardedWrites(),
            new StillClock(),
            events ?? new SilentEvents(),
            notices ?? new CountedNotices());

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    private static ProgrammeId Id(int network, int carried)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private static EventInformationTable DetailTable(int network, int carried)
    {
        byte[] heading = [0x1B, 0x28, 0x4A, .. "Heading"u8.ToArray()];
        byte[] body = [0x1B, 0x28, 0x4A, .. "Body"u8.ToArray()];
        byte[] items = [(byte)heading.Length, .. heading, (byte)body.Length, .. body];
        byte[] descriptor = [0x4E, (byte)(6 + items.Length), 0x00, 0x6A, 0x70, 0x6E, (byte)items.Length, .. items, 0x00];

        return Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.FirstScheduleActualTableId + 8,
                TableIdExtension = 1049,
                LastSectionNumber = 1,
                Body =
                [
                    0x7F, 0xE3,
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, (byte)(EventInformationTable.FirstScheduleActualTableId + 8),
                    (byte)(carried >> 8), (byte)(carried & 0xFF),
                    0xEF, 0x55, 0x22, 0x57, 0x00,
                    0x00, 0x03, 0x00,
                    0x00, (byte)descriptor.Length,
                    .. descriptor,
                ],
            }))).Table;
    }

    [Fact]
    public async Task AProgrammeThatRanLongTakesTheEndTheRunningTableGives()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);

        await writer.WriteAsync([Scheduled(network, 1, minutes: 3)], Cancel);

        ProgrammesWritten corrected = await writer.WriteAsync([Running(network, 1, minutes: 9)], Cancel);

        Assert.Equal(new ProgrammesWritten(0, 1, 0), corrected);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        Assert.Equal(TimeSpan.FromMinutes(9), stored!.EndsAt - stored.StartsAt);
        Assert.Equal(ProgrammeSource.PresentFollowing, stored.Source);
    }

    [Fact]
    public async Task AnOpenEndedRunningTableDoesNotEraseAnEndWeAlreadyKnow()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);

        await writer.WriteAsync([Scheduled(network, 1, minutes: 3)], Cancel);
        await writer.WriteAsync([Running(network, 1, minutes: null)], Cancel);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel);

        Assert.Equal(TimeSpan.FromMinutes(3), stored!.EndsAt - stored.StartsAt);
    }

    [Fact]
    public async Task AWriteThatChangedSomethingTellsTheScreensToLookAgain()
    {
        int network = NextNetwork();
        var events = new SilentEvents();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context, events: events);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Equal([AppEventName.Programs], events.Signalled);
    }

    [Fact]
    public async Task AWriteThatChangedNothingSaysNothing()
    {
        int network = NextNetwork();
        var events = new SilentEvents();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context, events: events);

        await writer.WriteAsync([Table(network, 1)], Cancel);
        events.Signalled.Clear();

        await writer.WriteAsync([Table(network, 1)], Cancel);

        Assert.Empty(events.Signalled);
    }

    [Fact]
    public async Task EveryChangeTakesTheNextRevisionAndNoChangeTakesNone()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        ProgrammeWriter writer = Writer(context);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        await using CarinaDbContext first = database.Open();
        long written = (await new ProgrammeRepository(first).FindAsync(Id(network, 1), Cancel))!.Revision;

        Assert.True(written > 0);

        await writer.WriteAsync([Table(network, 1)], Cancel);

        await using CarinaDbContext again = database.Open();

        Assert.Equal(written, (await new ProgrammeRepository(again).FindAsync(Id(network, 1), Cancel))!.Revision);

        await writer.WriteAsync([Table(network, 1, name: "ひるまえほっと")], Cancel);

        await using CarinaDbContext changed = database.Open();

        Assert.True((await new ProgrammeRepository(changed).FindAsync(Id(network, 1), Cancel))!.Revision > written);
    }

    [Fact]
    public async Task NoTwoProgrammesShareARevision()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Writer(context).WriteAsync([Table(network, 1), Table(network, 2)], Cancel);

        await using CarinaDbContext reading = database.Open();
        var repository = new ProgrammeRepository(reading);
        long one = (await repository.FindAsync(Id(network, 1), Cancel))!.Revision;
        long two = (await repository.FindAsync(Id(network, 2), Cancel))!.Revision;

        Assert.NotEqual(one, two);
    }

    private static EventInformationTable Scheduled(int network, int carried, int minutes)
        => Timed(network, carried, EventInformationTable.FirstScheduleActualTableId, minutes);

    private static EventInformationTable Running(int network, int carried, int? minutes)
        => Timed(network, carried, EventInformationTable.PresentFollowingActualTableId, minutes);

    private static EventInformationTable Timed(int network, int carried, int tableId, int? minutes)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = 1049,
                LastSectionNumber = 1,
                Body =
                [
                    0x7F, 0xE3,
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, (byte)tableId,
                    .. TimedEvent(carried, minutes),
                ],
            }))).Table;

    private static byte[] TimedEvent(int carried, int? minutes)
    {
        byte[] written = [.. "あさイチ".SelectMany(letter => Kanji(letter))];
        byte[] descriptor = [0x4D, (byte)(5 + written.Length), 0x6A, 0x70, 0x6E, (byte)written.Length, .. written, 0x00];
        byte[] duration = minutes is { } carriedMinutes
            ? [0x00, (byte)(((carriedMinutes / 10) << 4) | (carriedMinutes % 10)), 0x00]
            : [0xFF, 0xFF, 0xFF];

        return
        [
            (byte)(carried >> 8), (byte)(carried & 0xFF),
            0xEF, 0x55, 0x22, 0x57, 0x00,
            .. duration,
            0x00, (byte)descriptor.Length,
            .. descriptor,
        ];
    }

    private static EventInformationTable Table(
        int network,
        int carried,
        string name = "あさイチ",
        bool unreadableStart = false,
        byte[]? extra = null)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = 1049,
                LastSectionNumber = 1,
                Body =
                [
                    0x7F, 0xE3,
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, 0x4E,
                    .. Event(carried, name, unreadableStart, extra),
                ],
            }))).Table;

    private static byte[] Event(int carried, string name, bool unreadableStart, byte[]? extra = null)
    {
        byte[] written = [.. name.SelectMany(letter => Kanji(letter))];
        byte[] descriptor = [0x4D, (byte)(5 + written.Length), 0x6A, 0x70, 0x6E, (byte)written.Length, .. written, 0x00];
        byte[] carriedExtra = extra ?? [];

        return
        [
            (byte)(carried >> 8), (byte)(carried & 0xFF),
            0xEF, 0x55, unreadableStart ? (byte)0x2A : (byte)0x22, 0x57, 0x00,
            0x00, 0x03, 0x00,
            0x00, (byte)(descriptor.Length + carriedExtra.Length),
            .. descriptor,
            .. carriedExtra,
        ];
    }

    private static byte[] Kanji(char letter)
    {
        for (int row = JisX0208.FirstRow; row <= JisX0208.LastRow; row++)
        {
            for (int cell = 1; cell <= JisX0208.CellsPerRow; cell++)
            {
                if (JisX0208.TryMap(row, cell, out char mapped) && mapped == letter)
                {
                    return [(byte)(row + 0x20), (byte)(cell + 0x20)];
                }
            }
        }

        throw new InvalidOperationException($"No broadcast can send '{letter}'.");
    }
}
