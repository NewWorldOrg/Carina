using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AKeywordFindsWhatItAppearsInWhereverItAppears()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"ニュース{network}", "今日のできごと"), Cancel);
        await repository.AddAsync(Programme(network, 2, "大河ドラマ", $"続きはニュース{network}の後で"), Cancel);
        await repository.AddAsync(Programme(network, 3, "天気予報", "あすの空"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(Asking($"ニュース{network}"), Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([1, 2], found.Items.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task TheKeywordDoesNotCareAboutLetterCase()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"Morning NEWS{network}", string.Empty), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"news{network}"), Cancel)).Total);
    }

    [Fact]
    public async Task APageCarriesOnlyItsOwnSliceAndSaysHowManyThereAre()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        for (int carried = 1; carried <= 5; carried++)
        {
            await repository.AddAsync(Programme(network, carried, $"報道{network}の{carried}", string.Empty), Cancel);
        }

        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> second = await searches.SearchAsync(
            ProgrammeSearch.For($"報道{network}", At.AddHours(-1), At.AddDays(1), page: 2, perPage: 2)!,
            Cancel);

        Assert.Equal(5, second.Total);
        Assert.Equal(3, second.LastPage);
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task EveryWordOfTheKeywordHasToAppear()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"夏{network}の絶景", "海と山"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"夏{network}の思い出", "海と山"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(Asking($"夏{network} 絶景"), Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AWordOnlyTheSummaryCarriesIsNotFoundWhenOnlyTheTitleWasAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, "大河ドラマ", $"絶景{network}をめぐる"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"絶景{network}紀行", "ある町で"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"絶景{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AWordOnlyTheTitleCarriesIsNotFoundWhenOnlyTheSummaryWasAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"絶景{network}紀行", "ある町で"), Cancel);
        await repository.AddAsync(Programme(network, 2, "大河ドラマ", $"絶景{network}をめぐる"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"絶景{network}", new ProgrammeConditions { Fields = [ProgrammeField.Description] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnExcludedWordTakesTheProgrammeOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"紀行{network}", "再放送です"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "再放送" }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task EveryExcludedWordTakesItsOwnProgrammesOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"紀行{network}", "再放送です"), Cancel);
        await repository.AddAsync(Programme(network, 3, $"紀行{network} ダイジェスト", "まとめ"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "再放送 ダイジェスト" }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnExcludedWordIsLookedForInTheFieldsThatWereAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"紀行{network}", "再放送です"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"紀行{network}",
                new ProgrammeConditions { Exclude = "再放送", Fields = [ProgrammeField.Title] }),
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AGenreNarrowsToTheProgrammesFiledUnderIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}", 6), Cancel);
        await repository.AddAsync(Programme(network, 3, $"番組{network}", string.Empty), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Genres = [8] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnyOfTheGenresAskedForIsEnough()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}", 6), Cancel);
        await repository.AddAsync(Filed(network, 3, $"番組{network}", 4), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Genres = [8, 6] }),
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task AProgrammeFiledUnderSeveralGenresIsFoundByAnyOfThem()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8, 6), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"番組{network}", new ProgrammeConditions { Genres = [6] }),
                Cancel)).Total);
    }

    [Fact]
    public async Task AChannelNarrowsToWhatThatServiceBroadcast()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"番組{network}",
                new ProgrammeConditions { Channels = [new ProgrammeService(network, 1024)] }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1024, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task SeveralChannelsAreAskedForTogether()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1040, 3, $"番組{network}"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"番組{network}",
                new ProgrammeConditions
                {
                    Channels = [new ProgrammeService(network, 1024), new ProgrammeService(network, 1040)],
                }),
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task AResolvedBroadcastTypeNarrowsToTheServicesItCarries()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}").Over([new ProgrammeService(network, 1032)]),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1032, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task ABroadcastTypeThatCarriesNoServiceFindsNothingRatherThanEverything()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(0, (await searches.SearchAsync(Asking($"番組{network}").Over([]), Cancel)).Total);
    }

    [Fact]
    public async Task WithNoWordToLookForTheOtherConditionsStillNarrow()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Filed(network, 1, $"番組{network}の一", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}の二", 6), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            ProgrammeSearch.For(
                null,
                At.AddHours(-1),
                At.AddDays(1),
                conditions: new ProgrammeConditions
                {
                    Genres = [8],
                    Channels = [new ProgrammeService(network, 1049)],
                })!,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task WithNoWordToLookForAWordToLeaveOutStillTakesItsProgrammesOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"番組{network}の一", "はじめての放送"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"番組{network}の二", $"再放送{network}です"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            ProgrammeSearch.For(
                null,
                At.AddHours(-1),
                At.AddDays(1),
                conditions: new ProgrammeConditions
                {
                    Exclude = $"再放送{network}",
                    Channels = [new ProgrammeService(network, 1049)],
                })!,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AKeywordTypedInHalfWidthKanaFindsWhatWasBroadcastInFullWidth()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"ニュース{network}", "きょうのできごと"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"ﾆｭｰｽ{network}"), Cancel)).Total);
    }

    [Fact]
    public async Task AKeywordTypedInFullWidthKanaFindsWhatWasBroadcastInHalfWidth()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"ﾆｭｰｽ{network}", "きょうのできごと"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"ニュース{network}"), Cancel)).Total);
    }

    [Fact]
    public async Task AnEnclosedNumberIsFoundByTheNumberItEncloses()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"紀行{network}①", "はじまり"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"紀行{network}1"), Cancel)).Total);
    }

    [Fact]
    public async Task FullWidthLettersAreFoundByTheLettersTheyStandFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"ＮＥＷＳ{network}", "きょうのできごと"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"news{network}"), Cancel)).Total);
    }

    [Fact]
    public async Task NarrowingToTheTitleStillFindsItWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"ニュース{network}", "きょうのできごと"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
                Cancel)).Total);
    }

    [Fact]
    public async Task NarrowingToTheSummaryStillFindsItWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, "大河ドラマ", $"のちほどニュース{network}を"), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Description] }),
                Cancel)).Total);
    }

    [Fact]
    public async Task AWordToLeaveOutTakesTheProgrammeOutWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Programme(network, 2, $"紀行{network}", "ﾀﾞｲｼﾞｪｽﾄです"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "ダイジェスト" }),
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Theory]
    [InlineData("ﾆｭｰｽ", "ニュース")]
    [InlineData("ｷﾞｮｳｻﾞ", "ギョウザ")]
    [InlineData("ＮＥＷＳ", "news")]
    [InlineData("①②", "12")]
    [InlineData("㊤", "上")]
    [InlineData("㈱", "(株)")]
    [InlineData("㎏", "kg")]
    [InlineData("Ⅷ", "viii")]
    public async Task TheTextIsKeptAsBroadcastAndSearchedInItsCompatibilityShape(string broadcast, string searchable)
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);
        var searches = new ProgrammeSearchRepository(context);

        await repository.AddAsync(Programme(network, 1, broadcast, string.Empty), Cancel);
        await context.SaveChangesAsync(Cancel);

        await using CarinaDbContext reading = database.Open();
        Programme held = (await new ProgrammeRepository(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel))!;

        Assert.Equal(broadcast, held.Name);
        Assert.Equal(
            $"{searchable} ",
            reading.Entry(held).Property<string>(ProgrammeConfiguration.Searchable).CurrentValue);
    }

    private static ProgrammeSearch Asking(string keyword, ProgrammeConditions? conditions = null)
        => ProgrammeSearch.For(keyword, At.AddHours(-1), At.AddDays(1), conditions: conditions)!;

    private static Programme Programme(int network, int carried, string name, string summary)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(1),
            At.AddMinutes(carried),
            At.AddMinutes(carried + 30),
            name,
            summary,
            false));

    private static Programme Filed(int network, int carried, string name, params int[] genres)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(1),
            At.AddMinutes(carried),
            At.AddMinutes(carried + 30),
            name,
            string.Empty,
            false)
        {
            Genres = [.. genres.Select(kind => new ProgrammeGenre(kind, 0))],
        });

    private static Programme On(int network, int service, int carried, string name)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(1),
            At.AddMinutes(carried),
            At.AddMinutes(carried + 30),
            name,
            string.Empty,
            false));

    private static Programme Held(ProgrammeBroadcast broadcast)
        => Domain.Programmes.Programme.Discover(broadcast, At);
}
