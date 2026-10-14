using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SupersededProgrammeTests(RepositoryDatabase database)
{
    private static readonly DateTime Start = new(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime HeardTwoDaysBefore = Start.AddDays(-2);

    private static readonly DateTime HeardTheDayBefore = Start.AddDays(-1);

    private static readonly DateTime HeardToday = Start.AddHours(-1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Service = 1049;

    private const int Neighbour = 1050;

    [Fact]
    public async Task TheGuideLeavesOutTheScheduleAReplacementHeardLaterCoversAndTheRowsStay()
    {
        int network = BroadcastIds.NextNetwork();
        await KeepAsync(ReplacedSchedule.On(network, Service, Start), []);

        await using CarinaDbContext reading = database.Open();
        var programmes = new ProgrammeRepository(reading);
        IReadOnlyList<Programme> guide = await programmes.ListForServicesAsync(
            [new ProgrammeService(network, Service)],
            Start.AddHours(-2),
            Start.AddHours(6),
            Cancel);

        Assert.Equal(ReplacedSchedule.Standing, Events(guide));
        Assert.NotNull(await programmes.FindAsync(Id(network, Service, ReplacedSchedule.Replaced[0]), Cancel));
    }

    [Fact]
    public async Task AProgrammeTheReplacementSupersededHasNoDetailYetIsStillReadForReservations()
    {
        int network = BroadcastIds.NextNetwork();
        await KeepAsync(ReplacedSchedule.On(network, Service, Start), []);

        await using CarinaDbContext reading = database.Open();
        var programmes = new ProgrammeRepository(reading);

        Assert.Null(await programmes.FindUnlessSupersededAsync(
            Id(network, Service, ReplacedSchedule.Replaced[4]),
            Cancel));
        Assert.Equal(
            "差し替えの番組",
            (await programmes.FindUnlessSupersededAsync(Id(network, Service, ReplacedSchedule.Replacement), Cancel))?.Name);
        Assert.NotNull(await programmes.FindAsync(Id(network, Service, ReplacedSchedule.Replaced[4]), Cancel));
    }

    [Fact]
    public async Task TheProgrammeOnAirIsTheReplacementRatherThanTheOneItReplaced()
    {
        int network = BroadcastIds.NextNetwork();
        DateTime now = Start.AddMinutes(130);
        await KeepAsync(ReplacedSchedule.On(network, Service, Start), []);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<Programme> onAir = await new ProgrammeRepository(reading).ListAsync(
            new ProgrammeWindow(network, Service, now, now.AddMicroseconds(1)),
            Cancel);

        Assert.Equal([ReplacedSchedule.Replacement], Events(onAir));
    }

    [Fact]
    public async Task ASearchLeavesOutTheScheduleAReplacementHeardLaterCovers()
    {
        int network = BroadcastIds.NextNetwork();
        await KeepAsync(ReplacedSchedule.On(network, Service, Start), []);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Everything(network),
            Start.AddHours(-3),
            Cancel);

        Assert.Equal(ReplacedSchedule.Standing.Count, found.Total);
        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], found.Items.Select(match => match.Name));
    }

    [Fact]
    public async Task TheOldScheduleStaysLeftOutOfThePastDayAfterItAndItsReplacementMoveToTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        await KeepAsync([], [.. ReplacedSchedule.On(network, Service, Start).Select(Moved)]);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);

        Assert.Equal(ReplacedSchedule.Standing, Events(pastDay));
        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], Names(history));
        Assert.Equal(3, history.Total);
    }

    [Fact]
    public async Task AnOldScheduleKeptBeforeHeardTimesWereKeptIsLeftOutBesideAReplacementThatHasOne()
    {
        int network = BroadcastIds.NextNetwork();
        int stillInTheGuide = BroadcastIds.NextNetwork();
        Programme[] schedule = [.. ReplacedSchedule.On(network, Service, Start)];
        Programme[] another = [.. ReplacedSchedule.On(stillInTheGuide, Service, Start)];
        await KeepAsync(
            [another.Single(programme => programme.EventId.Value == ReplacedSchedule.Replacement)],
            [
                .. schedule.Select(programme => Unheard(Moved(programme), heardAnyway: programme.EventId.Value
                    is ReplacedSchedule.Replacement or ReplacedSchedule.After)),
                .. another
                    .Where(programme => ReplacedSchedule.Replaced.Contains(programme.EventId.Value))
                    .Select(programme => Unheard(Moved(programme))),
            ]);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);
        IReadOnlyList<ArchivedProgramme> besideTheGuide = await PastDayAsync(reading, stillInTheGuide);
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);
        PaginatedList<ProgrammeMatch> historyBesideTheGuide = await HistoryAsync(reading, stillInTheGuide);

        Assert.Equal(ReplacedSchedule.Standing, Events(pastDay));
        Assert.Empty(besideTheGuide);
        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], Names(history));
        Assert.Equal(["差し替えの番組"], Names(historyBesideTheGuide));
    }

    [Fact]
    public async Task TwoKeptProgrammesWithoutAHeardTimeBothStay()
    {
        int network = BroadcastIds.NextNetwork();
        await KeepAsync(
            [],
            [
                Unheard(Moved(On(network, 1, Start, Start.AddHours(1), null))),
                Unheard(Moved(On(network, 2, Start.AddMinutes(30), Start.AddHours(2), null))),
            ]);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);

        Assert.Equal([1, 2], Events(pastDay));
        Assert.Equal(2, history.Total);
    }

    [Fact]
    public async Task TheStoreLeavesOutTheSameProgrammesAsTheCode()
    {
        int network = BroadcastIds.NextNetwork();
        Programme[] held = Varied(network);
        ArchivedProgramme[] kept = Kept(network);
        await KeepAsync(held, kept);
        var code = new HeldProgrammes();
        code.Programmes.AddRange(held);
        ProgrammeService[] both = [new ProgrammeService(network, Service), new ProgrammeService(network, Neighbour)];

        await using CarinaDbContext reading = database.Open();
        var store = new ProgrammeRepository(reading);
        var storeArchive = new ArchivedProgrammeRepository(reading);
        var codeArchive = new HeldArchive(code);
        codeArchive.Programmes.AddRange(kept);
        IReadOnlyList<ArchivedProgramme> pastDays = await storeArchive.ListAsync(
            both,
            Start.AddDays(-3),
            Start,
            Cancel);

        Assert.Equal([41, 43, 44, 46, 47, 48, 49, 50, 51, 52], Events(pastDays));
        Assert.Equal(
            Faces(await codeArchive.ListAsync(both, Start.AddDays(-3), Start, Cancel)),
            Faces(pastDays));

        IReadOnlyList<Programme> guide = await store.ListForServicesAsync(
            both,
            Start.AddDays(-1),
            Start.AddDays(2),
            Cancel);

        Assert.Equal(
            [7, 9, 17, 19, 24, 25, 27, 28],
            Events(held.Where(programme => !programme.IsShadow)).Except(Events(guide)));
        Assert.Equal(
            Faces(await code.ListForServicesAsync(both, Start.AddDays(-1), Start.AddDays(2), Cancel)),
            Faces(guide));
        Assert.Equal(Faces(ProgrammeSupersession.Standing(held)), Faces(await FoundOneByOneAsync(store, held)));
        Assert.Equal(Faces(ProgrammeSupersession.Standing(held)), Faces(await FoundOneByOneAsync(code, held)));

        foreach (int minutes in new[] { -1440, 0, 15, 59, 60, 90, 130, 200, 245, 260 })
        {
            DateTime at = Start.AddMinutes(minutes);
            var moment = new ProgrammeWindow(network, Service, at, at.AddMicroseconds(1));

            Assert.Equal(
                Faces(Touching(await code.ListAsync(moment, Cancel), at)),
                Faces(await store.ListAsync(moment, Cancel)));
        }

        ProgrammeSearch search = Everything(network);
        PaginatedList<ProgrammeMatch> stored = await new ProgrammeSearchRepository(reading)
            .SearchAsync(search, Start, Cancel);
        PaginatedList<ProgrammeMatch> read = await new HeldSearches(code, codeArchive)
            .SearchAsync(search, Start, Cancel);

        Assert.Equal(read.Total, stored.Total);
        Assert.Equal(Names(read), Names(stored));
        Assert.Contains(stored.Items, match => match.IsArchived);
        Assert.Equal(Names(stored), await PageByPageAsync(new ProgrammeSearchRepository(reading), network));
    }

    private static Programme[] Varied(int network)
        =>
        [
            On(network, 1, Start, Start.AddHours(1), HeardToday),
            On(network, 2, Start.AddMinutes(30), Start.AddHours(2), HeardToday),

            On(network, 3, Start.AddHours(3), Start.AddHours(4), HeardToday),
            On(network, 4, Start.AddHours(2), Start.AddHours(3), HeardTheDayBefore),
            On(network, 5, Start.AddHours(4), Start.AddHours(5), HeardTheDayBefore),

            On(network, 6, Start.AddHours(6), null, HeardToday),
            On(network, 7, Start.AddHours(6).AddMinutes(59), Start.AddHours(7).AddMinutes(30), HeardTheDayBefore),
            On(network, 8, Start.AddHours(7), Start.AddHours(7).AddMinutes(30), HeardTheDayBefore),

            On(network, 9, Start.AddHours(9), null, HeardTheDayBefore),
            On(network, 10, Start.AddHours(9).AddMinutes(59), Start.AddHours(10).AddMinutes(30), HeardToday),
            On(network, 11, Start.AddHours(11), null, HeardTheDayBefore),
            On(network, 12, Start.AddHours(12), Start.AddHours(13), HeardToday),

            On(network, 13, Start.AddHours(14), Start.AddHours(15), HeardToday, shadow: true),
            On(network, 14, Start.AddHours(14), Start.AddHours(15), HeardTheDayBefore),
            On(network, 15, Start.AddHours(16), Start.AddHours(17), HeardTheDayBefore, shadow: true),
            On(network, 16, Start.AddHours(16), Start.AddHours(17), HeardToday),

            On(network, 17, Start.AddHours(18), Start.AddHours(19), null),
            On(network, 18, Start.AddHours(18).AddMinutes(30), Start.AddHours(20), HeardTheDayBefore),
            On(network, 19, Start.AddHours(19).AddMinutes(30), Start.AddHours(21), null),
            On(network, 20, Start.AddHours(20).AddMinutes(30), Start.AddHours(21), null),

            On(network, 21, Start.AddHours(22), Start.AddHours(23), HeardTheDayBefore, service: Neighbour),
            On(network, 22, Start.AddHours(22), Start.AddHours(23), HeardToday),

            On(network, 23, Start.AddHours(24), Start.AddHours(28).AddMinutes(10), HeardToday),
            On(network, 24, Start.AddHours(27).AddMinutes(5), Start.AddHours(28), HeardTheDayBefore),
            On(network, 25, Start.AddHours(28), Start.AddHours(28).AddMinutes(15), HeardTheDayBefore),
            On(network, 26, Start.AddHours(28).AddMinutes(10), Start.AddHours(29), HeardToday),

            On(network, 27, Start.AddHours(30), Start.AddHours(32), HeardTwoDaysBefore),
            On(network, 28, Start.AddHours(31), Start.AddHours(33), HeardTheDayBefore),
            On(network, 29, Start.AddHours(32).AddMinutes(30), Start.AddHours(34), HeardToday),

            On(network, 30, Start.AddHours(-20), Start.AddHours(-19), HeardTwoDaysBefore),
            On(network, 31, Start.AddHours(-19), Start.AddHours(-18), HeardToday),
        ];

    private static ArchivedProgramme[] Kept(int network)
        =>
        [
            Kept(network, 90, Start.AddHours(-20).AddMinutes(30), Start.AddHours(-19).AddMinutes(30), null),

            Kept(network, 41, Start.AddHours(-48), Start.AddHours(-47), HeardToday.AddDays(-2)),
            Kept(network, 42, Start.AddHours(-48), Start.AddHours(-47), HeardTwoDaysBefore.AddDays(-2)),
            Kept(network, 43, Start.AddHours(-47), Start.AddHours(-46), HeardTwoDaysBefore.AddDays(-2)),

            Kept(network, 44, Start.AddHours(-45), Start.AddHours(-44), null),
            Kept(network, 45, Start.AddHours(-44).AddMinutes(30), Start.AddHours(-43), null),
            Kept(network, 46, Start.AddHours(-44).AddMinutes(30), Start.AddHours(-43), HeardTheDayBefore.AddDays(-2)),

            Kept(network, 47, Start.AddHours(-30), Start.AddHours(-29), HeardTwoDaysBefore),
            Kept(network, 48, Start.AddHours(-30), Start.AddHours(-29), HeardTwoDaysBefore, service: Neighbour),
            Kept(network, 49, Start.AddHours(-29), Start.AddHours(-28), null),
            Kept(network, 50, Start.AddHours(-29).AddMinutes(30), Start.AddHours(-28), null),

            Kept(network, 51, Start.AddHours(-26), Start.AddHours(-25), HeardTwoDaysBefore),
            Kept(network, 52, Start.AddHours(-25), Start.AddHours(-24), HeardTwoDaysBefore),
        ];

    private static ArchivedProgramme Kept(
        int network,
        int eventId,
        DateTime startsAt,
        DateTime endsAt,
        DateTime? heard,
        int service = Service)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(service),
            new EventId(eventId),
            startsAt,
            endsAt,
            $"保存された番組{eventId}",
            string.Empty,
            false,
            [],
            [],
            Start,
            heard);

    private static ArchivedProgramme Moved(Programme programme)
        => ArchivedProgramme.Of(new EndedProgramme(programme, programme.EndsAt!.Value), Start.AddDays(2))!;

    private static ArchivedProgramme Unheard(ArchivedProgramme kept, bool heardAnyway = false)
        => heardAnyway
            ? kept
            : ArchivedProgramme.Rehydrate(
                kept.NetworkId,
                kept.ServiceId,
                kept.EventId,
                kept.StartsAt,
                kept.EndsAt,
                kept.Name,
                kept.Summary,
                kept.HasSubtitles,
                kept.Genres,
                kept.Items,
                kept.ArchivedAt);

    private static async Task<IReadOnlyList<ArchivedProgramme>> PastDayAsync(CarinaDbContext reading, int network)
        => await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, Service)],
            Start.AddHours(-2),
            Start.AddHours(6),
            Cancel);

    private static async Task<PaginatedList<ProgrammeMatch>> HistoryAsync(CarinaDbContext reading, int network)
        => await new ProgrammeSearchRepository(reading).SearchAsync(Everything(network), Start.AddDays(2), Cancel);

    private static async Task<string[]> PageByPageAsync(ProgrammeSearchRepository searches, int network)
    {
        List<string> read = [];

        for (int page = 1; page <= 20; page++)
        {
            ProgrammeSearch asking = ProgrammeSearch.For(
                null,
                Start.AddDays(-3),
                Start.AddDays(3),
                page: page,
                perPage: 4,
                conditions: new ProgrammeConditions
                {
                    Channels = [new ProgrammeService(network, Service), new ProgrammeService(network, Neighbour)],
                })!;
            PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(asking, Start, Cancel);

            read.AddRange(Names(found));

            if (found.Items.Count < 4)
            {
                break;
            }
        }

        return [.. read];
    }

    private async Task KeepAsync(IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept)
    {
        await using CarinaDbContext writing = database.Open();
        var programmes = new ProgrammeRepository(writing);

        foreach (Programme programme in held)
        {
            await programmes.AddAsync(programme, Cancel);
        }

        await new ArchivedProgrammeRepository(writing).KeepAsync(kept, Cancel);
    }

    private static async Task<IReadOnlyList<Programme>> FoundOneByOneAsync(
        IProgrammeRepository programmes,
        IReadOnlyList<Programme> held)
    {
        List<Programme> found = [];

        foreach (Programme programme in held)
        {
            if (await programmes.FindUnlessSupersededAsync(programme.Id, Cancel) is { } standing)
            {
                found.Add(standing);
            }
        }

        return found;
    }

    private static string[] Faces(IEnumerable<ArchivedProgramme> programmes)
        =>
        [
            .. programmes
                .Select(programme => $"{programme.ServiceId.Value}-{programme.EventId.Value}")
                .Order(StringComparer.Ordinal),
        ];

    private static int[] Events(IEnumerable<ArchivedProgramme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    private static IEnumerable<Programme> Touching(IReadOnlyList<Programme> programmes, DateTime at)
        => programmes.Where(programme => programme.StartsAt <= at && (programme.EndsAt is not { } ends || ends > at));

    private static ProgrammeSearch Everything(int network)
        => ProgrammeSearch.For(
            null,
            Start.AddDays(-3),
            Start.AddDays(3),
            perPage: 100,
            conditions: new ProgrammeConditions
            {
                Channels = [new ProgrammeService(network, Service), new ProgrammeService(network, Neighbour)],
            })!;

    private static string[] Faces(IEnumerable<Programme> programmes)
        =>
        [
            .. programmes
                .Select(programme => $"{programme.ServiceId.Value}-{programme.EventId.Value}")
                .Order(StringComparer.Ordinal),
        ];

    private static string[] Names(PaginatedList<ProgrammeMatch> found)
        => [.. found.Items.Select(match => match.Name)];

    private static int[] Events(IEnumerable<Programme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    private static ProgrammeId Id(int network, int service, int eventId)
        => new(new NetworkId(network), new ServiceId(service), new EventId(eventId));

    private static Programme On(
        int network,
        int eventId,
        DateTime startsAt,
        DateTime? endsAt,
        DateTime? heard,
        bool shadow = false,
        int service = Service)
        => Programme.Rehydrate(
            Id(network, service, eventId),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            shadow ? string.Empty : $"番組{eventId:D2}",
            string.Empty,
            shadow,
            HeardTwoDaysBefore,
            lastHeardAt: heard);
}
