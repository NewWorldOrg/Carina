using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchAcrossLayersTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AProgrammeThatHasMovedToTheArchiveIsStillFoundByItsKeyword()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Archived(network, 1, $"紀行{network}", "むかしの放送")],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"紀行{network}"),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.True(found.Items[0].IsArchived);
        Assert.Null(found.Items[0].Revision);
        Assert.Null(found.Items[0].Source);
    }

    [Fact]
    public async Task OnePageCarriesBothLayersInOneOrder()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", startsAt: At.AddHours(-3)),
                Archived(network, 3, $"紀行{network}", startsAt: At.AddHours(-1)),
            ],
            Cancel);
        await programmes.AddAsync(Programme(network, 2, $"紀行{network}", string.Empty, At.AddHours(-2)), Cancel);
        await programmes.AddAsync(Programme(network, 4, $"紀行{network}", string.Empty, At), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"紀行{network}"),
            Cancel);

        Assert.Equal(4, found.Total);
        Assert.Equal([1, 2, 3, 4], found.Items.Select(match => match.EventId.Value));
        Assert.Equal([true, false, true, false], found.Items.Select(match => match.IsArchived));
    }

    [Fact]
    public async Task APageCountsBothLayersAndSlicesAcrossTheBoundary()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", startsAt: At.AddHours(-3)),
                Archived(network, 2, $"紀行{network}", startsAt: At.AddHours(-2)),
            ],
            Cancel);
        await programmes.AddAsync(Programme(network, 3, $"紀行{network}", string.Empty, At.AddHours(-1)), Cancel);
        await programmes.AddAsync(Programme(network, 4, $"紀行{network}", string.Empty, At), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> second = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"紀行{network}", At.AddDays(-1), At.AddDays(1), page: 2, perPage: 2)!,
            Cancel);

        Assert.Equal(4, second.Total);
        Assert.Equal([3, 4], second.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task ASpanThatEndsBeforeTheArchivedProgrammeLeavesItOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Archived(network, 1, $"紀行{network}", startsAt: At.AddDays(-20))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        var searches = new ProgrammeSearchRepository(reading);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"紀行{network}", At.AddDays(-30), At, page: 1)!,
                Cancel)).Total);
        Assert.Equal(
            0,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"紀行{network}", At.AddDays(-3), At, page: 1)!,
                Cancel)).Total);
    }

    [Fact]
    public async Task AGenreNarrowsTheArchivedProgrammesToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Filed(network, 1, $"番組{network}", 8),
                Filed(network, 2, $"番組{network}", 6),
            ],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Genres = [8] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AChannelNarrowsTheArchivedProgrammesToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Archived(network, 1, $"番組{network}"),
                Archived(network, 2, $"番組{network}", service: 1050),
            ],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Channels = [new ProgrammeService(network, 1050)] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1050, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task AWordToLeaveOutTakesTheArchivedProgrammeOutToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", "はじめての放送"),
                Archived(network, 2, $"紀行{network}", "再放送です"),
            ],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "再放送" }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task NarrowingToTheTitleReachesTheArchiveInTheSameShape()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [
                Archived(network, 1, $"ニュース{network}", "きょうのできごと"),
                Archived(network, 2, "大河ドラマ", $"のちほどニュース{network}を"),
            ],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeHeldInBothLayersIsCarriedOnceAndTheHeldOneWins()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Archived(network, 1, $"紀行{network}", "写しの概要", startsAt: At)],
            Cancel);
        await programmes.AddAsync(Programme(network, 1, $"紀行{network}", "番組表の概要", At), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"紀行{network}"),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.False(found.Items[0].IsArchived);
        Assert.Equal("番組表の概要", found.Items[0].Summary);
    }

    [Fact]
    public async Task TheSameEventNumberUsedAgainLaterKeepsBothTheHeldAndTheArchivedOne()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Archived(network, 1, $"紀行{network}", startsAt: At.AddDays(-10))],
            Cancel);
        await programmes.AddAsync(Programme(network, 1, $"紀行{network}", string.Empty, At), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"紀行{network}", At.AddDays(-30), At.AddDays(1))!,
            Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([true, false], found.Items.Select(match => match.IsArchived));
    }

    [Fact]
    public async Task SortingByNameOrdersAcrossBothLayers()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Archived(network, 1, $"番組{network} B", startsAt: At.AddHours(-3))],
            Cancel);
        await programmes.AddAsync(Programme(network, 2, $"番組{network} A", string.Empty, At), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"番組{network}", At.AddDays(-1), At.AddDays(1), ProgrammeSort.Name)!,
            Cancel);

        Assert.Equal([2, 1], found.Items.Select(match => match.EventId.Value));
    }

    private static ProgrammeSearch Asking(string keyword, ProgrammeConditions? conditions = null)
        => ProgrammeSearch.For(keyword, At.AddDays(-30), At.AddDays(1), conditions: conditions)!;

    private static ArchivedProgramme Archived(
        int network,
        int carried,
        string name,
        string summary = "",
        int service = 1049,
        DateTime? startsAt = null)
    {
        DateTime began = startsAt ?? At.AddHours(-2);

        return ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(service),
            new EventId(carried),
            began,
            began.AddMinutes(30),
            name,
            summary,
            false,
            [],
            [],
            At);
    }

    private static ArchivedProgramme Filed(int network, int carried, string name, params int[] genres)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(1049),
            new EventId(carried),
            At.AddHours(-2),
            At.AddHours(-1),
            name,
            string.Empty,
            false,
            [.. genres.Select(kind => new ProgrammeGenre(kind, 0))],
            [],
            At);

    private static Programme Programme(int network, int carried, string name, string summary, DateTime began)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(1),
                began,
                began.AddMinutes(30),
                name,
                summary,
                false),
            At);
}
