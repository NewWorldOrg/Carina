using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchLooksForwardTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASearchThatNamesNoSpanLeavesOutWhatHasFinishedBroadcasting()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            Asking($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task WhatIsOnTheAirAtThisVeryInstantIsStillToCome()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddMinutes(-10), Now.AddMinutes(20)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            Asking($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsNeverOver()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-5), null), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            Asking($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeThatEndsAtThisVeryInstantIsOverAndOneMomentLaterIsNot()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-1), Now), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(-1), Now.AddMicroseconds(1)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            Asking($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task ASpanThatReachesBackKeepsWhatHasFinishedBroadcasting()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            ProgrammeSearch.For($"報道{network}", Now.AddDays(-1), Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanDoesNotReadTheArchiveWhateverTheTimesInItSay()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            Asking($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task ASpanThatOnlyLooksAheadDoesNotReadTheArchiveEither()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"報道{network}", Now.AddHours(1), Now.AddDays(5))!,
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task ASpanThatReachesBackReadsTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(-2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"報道{network}", Now.AddDays(-5), Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.True(found.Items[0].IsArchived);
    }

    [Fact]
    public async Task ASpanThatBeginsAtThisVeryInstantDoesNotReachBack()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        var searches = new ProgrammeSearchRepository(reading);

        Assert.Equal(
            0,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"報道{network}", Now, Now.AddDays(5))!,
                Now,
                Cancel)).Total);
        Assert.Equal(
            1,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"報道{network}", Now.AddTicks(-1), Now.AddDays(5))!,
                Now,
                Cancel)).Total);
    }

    [Fact]
    public async Task AnEndOnItsOwnDoesNotReadTheArchiveWhateverTheTimesInItSay()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddHours(2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For($"報道{network}", null, Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AnEndOnItsOwnRunsFromNowRatherThanFromTheStartOfTheRecord()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await programmes.AddAsync(Ran(network, 3, $"報道{network}", Now.AddDays(2), Now.AddDays(2).AddMinutes(30)), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(context).SearchAsync(
            ProgrammeSearch.For($"報道{network}", null, Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task ABeginningOnItsOwnReachesBackOnlyWhenItFallsInThePast()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using CarinaDbContext reading = database.Open();
        var searches = new ProgrammeSearchRepository(reading);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"報道{network}", Now.AddDays(-1), null)!,
                Now,
                Cancel)).Total);
        Assert.Equal(
            0,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"報道{network}", Now.AddDays(1), null)!,
                Now,
                Cancel)).Total);
    }

    [Fact]
    public async Task AnExcludedWordOnItsOwnStillLooksForwardRatherThanWalkingTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [Kept(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 3, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        PaginatedList<ProgrammeMatch> found = await new ProgrammeSearchRepository(reading).SearchAsync(
            ProgrammeSearch.For(
                null,
                null,
                null,
                conditions: new ProgrammeConditions
                {
                    Exclude = "再放送",
                    Channels = [new ProgrammeService(network, 1049)],
                })!,
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(3, found.Items[0].EventId.Value);
    }

    private static ProgrammeSearch Asking(string keyword)
        => ProgrammeSearch.For(keyword, null, null)!;

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

    private static ArchivedProgramme Kept(int network, int carried, string name, DateTime began)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(1049),
            new EventId(carried),
            began,
            began.AddMinutes(30),
            name,
            string.Empty,
            false,
            [],
            [],
            Now);
}
