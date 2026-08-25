using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchSpanEndsTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime From = Now.AddDays(-3);

    private static readonly DateTime To = Now.AddDays(3);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AProgrammeThatEndsWhereTheSpanBeginsIsOutsideItAndOneMomentLaterIsInside()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddHours(-1), From), Cancel);
        await programmes.AddAsync(
            Ran(network, 2, $"報道{network}", From.AddHours(-1), From.AddMicroseconds(1)),
            Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeThatBeginsWhereTheSpanEndsIsOutsideItAndOneMomentEarlierIsInside()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To, To.AddHours(1)), Cancel);
        await programmes.AddAsync(
            Ran(network, 2, $"報道{network}", To.AddMicroseconds(-1), To.AddHours(1)),
            Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeThatFinishedAnHourBeforeTheSpanBeganIsNotDraggedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddHours(-2), From.AddHours(-1)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AProgrammeThatBeginsAnHourAfterTheSpanEndedIsNotDraggedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To.AddHours(1), To.AddHours(2)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsInsideASpanItBeganBefore()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddDays(-1), null), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsStillOutsideASpanItBeginsAfter()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To.AddHours(1), null), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(context, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AnArchivedProgrammeIsHeldToTheSameSpanEndsAsAHeldOne()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Kept(network, 1, $"報道{network}", From.AddHours(-1), From),
                Kept(network, 2, $"報道{network}", From.AddHours(-1), From.AddMicroseconds(1)),
                Kept(network, 3, $"報道{network}", To, To.AddHours(1)),
                Kept(network, 4, $"報道{network}", To.AddMicroseconds(-1), To.AddHours(1)),
            ],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await Searching(reading, network);

        Assert.Equal([2, 4], found.Items.Select(match => match.EventId.Value));
    }

    private static Task<PaginatedList<ProgrammeMatch>> Searching(CarinaDbContext context, int network)
        => new ProgrammeSearchRepository(context).SearchAsync(
            ProgrammeSearch.For($"報道{network}", From, To)!,
            Now,
            Cancel);

    private static Programme Ran(int network, int carried, string name, DateTime began, DateTime? ended)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(1),
                began,
                ended,
                name,
                string.Empty,
                false),
            Now);

    private static ArchivedProgramme Kept(int network, int carried, string name, DateTime began, DateTime ended)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(1049),
            new EventId(carried),
            began,
            ended,
            name,
            string.Empty,
            false,
            [],
            [],
            Now);
}
