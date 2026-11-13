using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Programmes;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const int Listed = 1049;

    private const int Withheld = 1032;

    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly Dictionary<string, Func<int, ProgrammeSearch>> Asked = new(StringComparer.Ordinal)
    {
        ["the hour spelled in half width"] = network => Ask(network, "100017時"),
        ["the hour spelled in full width"] = network => Ask(network, "10001７時"),
        ["a name in half width kana"] = network => Ask(network, "ﾆｭｰｽ"),
        ["a name in full width kana"] = network => Ask(network, "ニュース"),
        ["a voiced kana written apart"] = network => Ask(network, "ｷﾞｮｳｻﾞ"),
        ["a voiced kana written together"] = network => Ask(network, "ギョウザ"),
        ["latin in one case"] = network => Ask(network, "news"),
        ["latin in the other case"] = network => Ask(network, "ＮＥＷＳ"),
        ["two words that both have to appear"] = network => Ask(network, "夏 絶景"),
        ["two words where one is missing"] = network => Ask(network, "夏の 冬の"),
        ["a word nothing carries"] = network => Ask(network, "該当なし"),
        ["a word left out"] = network => Ask(network, "夏の", new ProgrammeConditions { Exclude = "思い出" }),
        ["only the title"] = network => Ask(
            network,
            "絶景",
            new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
        ["only the summary"] = network => Ask(
            network,
            "絶景",
            new ProgrammeConditions { Fields = [ProgrammeField.Description] }),
        ["only the title, left out"] = network => Ask(
            network,
            "夏の",
            new ProgrammeConditions { Exclude = "絶景", Fields = [ProgrammeField.Title] }),
        ["only the summary, left out"] = network => Ask(
            network,
            "夏の",
            new ProgrammeConditions { Exclude = "絶景", Fields = [ProgrammeField.Description] }),
        ["one genre"] = network => Ask(network, string.Empty, new ProgrammeConditions { Genres = [8] }),
        ["two genres"] = network => Ask(network, string.Empty, new ProgrammeConditions { Genres = [6, 8] }),
        ["a genre nothing is filed under"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Genres = [3] }),
        ["one sub genre"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { SubGenres = [new ProgrammeGenre(8, 1)] }),
        ["a sub genre nothing is filed under"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { SubGenres = [new ProgrammeGenre(8, 7)] }),
        ["a sub genre beside the whole of another genre"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Genres = [6], SubGenres = [new ProgrammeGenre(8, 1)] }),
        ["one day of the week"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Days = [DayOfWeek.Tuesday] }),
        ["a day the clock names that the broadcast day does not"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Days = [DayOfWeek.Wednesday] }),
        ["two days of the week"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Days = [DayOfWeek.Saturday, DayOfWeek.Sunday] }),
        ["one channel"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Channels = [new ProgrammeService(network, Listed)] }),
        ["a channel that carries nothing"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Channels = [new ProgrammeService(network, 1)] }),
        ["a mark asked for"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Marks = [ProgrammeMark.New] }),
        ["two marks both of which are needed"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Marks = [ProgrammeMark.New, ProgrammeMark.Rerun] }),
        ["a mark in the archive"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Marks = [ProgrammeMark.Final] }),
        ["a mark left out"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { ExcludedMarks = [ProgrammeMark.Rerun] }),
        ["a mark asked for and another left out"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Marks = [ProgrammeMark.New], ExcludedMarks = [ProgrammeMark.Rerun] }),
        ["a mark beside a word"] = network => Ask(
            network,
            "アニメ",
            new ProgrammeConditions { Marks = [ProgrammeMark.New] }),
        ["a mark nothing carries"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Marks = [ProgrammeMark.Weather] }),
        ["a mark left out while the words are looked for in the title alone"] = network => Ask(
            network,
            "アニメ",
            new ProgrammeConditions { Fields = [ProgrammeField.Title], ExcludedMarks = [ProgrammeMark.Rerun] }),
        ["a per cent sign that came in as a word"] = network => Ask(network, "100%"),
        ["a full width per cent sign"] = network => Ask(network, "夏％"),
        ["an underscore"] = network => Ask(network, "夏＿"),
        ["a backslash before a per cent"] = network => Ask(network, "夏\\%"),
        ["a backslash at the end"] = network => Ask(network, "夏\\"),
        ["a mark that unfolds into a space"] = network => Ask(network, "終¨"),
        ["a word left out of the summary alone"] = network => Ask(
            network,
            "作り方",
            new ProgrammeConditions { Exclude = "ギョウザ", Fields = [ProgrammeField.Description] }),
        ["a word left out of the title alone"] = network => Ask(
            network,
            "作り方",
            new ProgrammeConditions { Exclude = "ギョウザ", Fields = [ProgrammeField.Title] }),
        ["a search that names no span at all"] = network => ProgrammeSearch.For(
            $"n{network}",
            null,
            null,
            conditions: new ProgrammeConditions { Channels = Everywhere(network) })!,
        ["a span that stops short of the archive"] = network => Ask(
            network,
            string.Empty,
            null,
            At.AddHours(-1),
            At.AddDays(1)),
        ["sorted by name"] = network => Ask(network, string.Empty, null, sort: ProgrammeSort.Name),
        ["sorted by name backwards"] = network => Ask(
            network,
            string.Empty,
            null,
            sort: ProgrammeSort.Name,
            descending: true),
        ["sorted by start backwards"] = network => Ask(network, string.Empty, null, descending: true),
        ["a later page"] = network => Ask(network, string.Empty, null, page: 2, perPage: 2),
        ["only what one broadcast type carries"] = network => Ask(network, string.Empty)
            .Over([new ProgrammeService(network, Listed)]),
        ["a broadcast type that carries nothing"] = network => Ask(network, string.Empty).Over([]),
        ["what the guide does not list, left out"] = network => Ask(network, string.Empty)
            .Except([new ProgrammeService(network, Withheld)]),
    };

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime From = Now.AddDays(-3);

    private static readonly DateTime To = Now.AddDays(3);

    private readonly ServiceProvider container = database.Infrastructure();

    public static TheoryData<string> WhatIsAsked()
    {
        var carried = new TheoryData<string>();

        foreach (string named in Asked.Keys)
        {
            carried.Add(named);
        }

        return carried;
    }

    [Fact]
    public async Task AKeywordFindsWhatItAppearsInWhereverItAppears()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"ニュース{network}", "今日のできごと"), Cancel);
        await repository.AddAsync(Described(network, 2, "大河ドラマ", $"続きはニュース{network}の後で"), Cancel);
        await repository.AddAsync(Described(network, 3, "天気予報", "あすの空"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(Asking($"ニュース{network}"), At, Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([1, 2], found.Items.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task TheKeywordDoesNotCareAboutLetterCase()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"Morning NEWS{network}", string.Empty), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"news{network}"), At, Cancel)).Total);
    }

    [Fact]
    public async Task APageCarriesOnlyItsOwnSliceAndSaysHowManyThereAre()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        for (int carried = 1; carried <= 5; carried++)
        {
            await repository.AddAsync(Described(network, carried, $"報道{network}の{carried}", string.Empty), Cancel);
        }

        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> second = await searches.SearchAsync(
            ProgrammeSearch.For($"報道{network}", At.AddHours(-1), At.AddDays(1), page: 2, perPage: 2)!,
            At,
            Cancel);

        Assert.Equal(5, second.Total);
        Assert.Equal(3, second.LastPage);
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task EveryWordOfTheKeywordHasToAppear()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"夏{network}の絶景", "海と山"), Cancel);
        await repository.AddAsync(Described(network, 2, $"夏{network}の思い出", "海と山"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(Asking($"夏{network} 絶景"), At, Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AWordOnlyTheSummaryCarriesIsNotFoundWhenOnlyTheTitleWasAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, "大河ドラマ", $"絶景{network}をめぐる"), Cancel);
        await repository.AddAsync(Described(network, 2, $"絶景{network}紀行", "ある町で"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"絶景{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AWordOnlyTheTitleCarriesIsNotFoundWhenOnlyTheSummaryWasAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"絶景{network}紀行", "ある町で"), Cancel);
        await repository.AddAsync(Described(network, 2, "大河ドラマ", $"絶景{network}をめぐる"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"絶景{network}", new ProgrammeConditions { Fields = [ProgrammeField.Description] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnExcludedWordTakesTheProgrammeOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Described(network, 2, $"紀行{network}", "再放送です"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "再放送" }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task EveryExcludedWordTakesItsOwnProgrammesOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Described(network, 2, $"紀行{network}", "再放送です"), Cancel);
        await repository.AddAsync(Described(network, 3, $"紀行{network} ダイジェスト", "まとめ"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "再放送 ダイジェスト" }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnExcludedWordIsLookedForInTheFieldsThatWereAskedFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"紀行{network}", "再放送です"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"紀行{network}",
                new ProgrammeConditions { Exclude = "再放送", Fields = [ProgrammeField.Title] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AGenreNarrowsToTheProgrammesFiledUnderIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}", 6), Cancel);
        await repository.AddAsync(Described(network, 3, $"番組{network}", string.Empty), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Genres = [8] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AnyOfTheGenresAskedForIsEnough()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}", 6), Cancel);
        await repository.AddAsync(Filed(network, 3, $"番組{network}", 4), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}", new ProgrammeConditions { Genres = [8, 6] }),
            At,
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task AProgrammeFiledUnderSeveralGenresIsFoundByAnyOfThem()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Filed(network, 1, $"番組{network}", 8, 6), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"番組{network}", new ProgrammeConditions { Genres = [6] }),
                At,
                Cancel)).Total);
    }

    [Fact]
    public async Task AChannelNarrowsToWhatThatServiceBroadcast()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"番組{network}",
                new ProgrammeConditions { Channels = [new ProgrammeService(network, 1024)] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1024, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task SeveralChannelsAreAskedForTogether()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1040, 3, $"番組{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking(
                $"番組{network}",
                new ProgrammeConditions
                {
                    Channels = [new ProgrammeService(network, 1024), new ProgrammeService(network, 1040)],
                }),
            At,
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task AResolvedBroadcastTypeNarrowsToTheServicesItCarries()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await repository.AddAsync(On(network, 1032, 2, $"番組{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"番組{network}").Over([new ProgrammeService(network, 1032)]),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1032, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task ABroadcastTypeThatCarriesNoServiceFindsNothingRatherThanEverything()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(On(network, 1024, 1, $"番組{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(0, (await searches.SearchAsync(Asking($"番組{network}").Over([]), At, Cancel)).Total);
    }

    [Fact]
    public async Task WithNoWordToLookForTheOtherConditionsStillNarrow()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Filed(network, 1, $"番組{network}の一", 8), Cancel);
        await repository.AddAsync(Filed(network, 2, $"番組{network}の二", 6), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

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
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task WithNoWordToLookForAWordToLeaveOutStillTakesItsProgrammesOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"番組{network}の一", "はじめての放送"), Cancel);
        await repository.AddAsync(Described(network, 2, $"番組{network}の二", $"再放送{network}です"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

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
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AKeywordTypedInHalfWidthKanaFindsWhatWasBroadcastInFullWidth()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"ニュース{network}", "きょうのできごと"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"ﾆｭｰｽ{network}"), At, Cancel)).Total);
    }

    [Fact]
    public async Task AKeywordTypedInFullWidthKanaFindsWhatWasBroadcastInHalfWidth()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"ﾆｭｰｽ{network}", "きょうのできごと"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"ニュース{network}"), At, Cancel)).Total);
    }

    [Fact]
    public async Task AnEnclosedNumberIsFoundByTheNumberItEncloses()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"紀行{network}①", "はじまり"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"紀行{network}1"), At, Cancel)).Total);
    }

    [Fact]
    public async Task FullWidthLettersAreFoundByTheLettersTheyStandFor()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"ＮＥＷＳ{network}", "きょうのできごと"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(1, (await searches.SearchAsync(Asking($"news{network}"), At, Cancel)).Total);
    }

    [Fact]
    public async Task NarrowingToTheTitleStillFindsItWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"ニュース{network}", "きょうのできごと"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
                At,
                Cancel)).Total);
    }

    [Fact]
    public async Task NarrowingToTheSummaryStillFindsItWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, "大河ドラマ", $"のちほどニュース{network}を"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                Asking($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Description] }),
                At,
                Cancel)).Total);
    }

    [Fact]
    public async Task AWordToLeaveOutTakesTheProgrammeOutWhicheverShapeItWasTypedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"紀行{network}", "はじめての放送"), Cancel);
        await repository.AddAsync(Described(network, 2, $"紀行{network}", "ﾀﾞｲｼﾞｪｽﾄです"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"紀行{network}", new ProgrammeConditions { Exclude = "ダイジェスト" }),
            At,
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, broadcast, string.Empty), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Programme held = (await ProgrammesIn(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel))!;

        Assert.Equal(broadcast, held.Name);
        Assert.Equal(
            $"{searchable} ",
            ContextIn(reading).Entry(held).Property<string>(ProgrammeConfiguration.Searchable).CurrentValue);
    }

    [Fact]
    public async Task ASkeletonEventIsNeverAmongTheMatches()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Described(network, 1, $"報道{network}", string.Empty), Cancel);
        await repository.AddAsync(Skeleton(network, 2, $"報道{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(Asking($"報道{network}"), At, Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal([1], found.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task AWithheldServiceIsDroppedAndTheRestAreLeftWhereTheyAre()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(On(network, 1049, 1, $"報道{network}"), Cancel);
        await repository.AddAsync(On(network, 1080, 2, $"報道{network}"), Cancel);
        await repository.AddAsync(On(network, 1091, 3, $"報道{network}"), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            Asking($"報道{network}").Except([new ProgrammeService(network, 1080)]),
            At,
            Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([1, 3], found.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task TheDayTheStoreWorksOutIsTheDayTheCodeWorksOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        DateTime began = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime[] instants =
        [
            .. Enumerable.Range(0, 60).Select(carried => began.AddHours(carried * 143)),
        ];

        for (int carried = 0; carried < instants.Length; carried++)
        {
            await repository.AddAsync(Beginning(network, carried + 1, instants[carried]), Cancel);
        }

        await ContextIn(scope).SaveChangesAsync(Cancel);

        Dictionary<int, int> stored = await ContextIn(scope).Set<ProgrammeMatch>()
            .Where(match => match.NetworkId == new NetworkId(network))
            .Select(match => new
            {
                Event = match.EventId,
                Day = EF.Property<int>(match, ProgrammeConfiguration.BroadcastDayOfWeek),
            })
            .ToDictionaryAsync(row => row.Event.Value, row => row.Day, Cancel);

        Assert.Equal(instants.Length, stored.Count);
        Assert.All(
            Enumerable.Range(0, instants.Length),
            carried => Assert.Equal((int)BroadcastDay.Of(instants[carried]), stored[carried + 1]));
    }

    [Fact]
    public async Task ASubGenreIsFoundWithoutTheRestOfItsGenreComingWithIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository repository = ProgrammesIn(scope);
        IProgrammeSearchRepository searches = SearchesIn(scope);

        await repository.AddAsync(Under(network, 1, $"報道{network}", new ProgrammeGenre(8, 0)), Cancel);
        await repository.AddAsync(Under(network, 2, $"報道{network}", new ProgrammeGenre(8, 2)), Cancel);
        await repository.AddAsync(Under(network, 3, $"報道{network}", new ProgrammeGenre(6, 2)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> narrowed = await searches.SearchAsync(
            Asking($"報道{network}", new ProgrammeConditions { SubGenres = [new ProgrammeGenre(8, 2)] }),
            At,
            Cancel);
        PaginatedList<ProgrammeMatch> whole = await searches.SearchAsync(
            Asking($"報道{network}", new ProgrammeConditions { Genres = [8] }),
            At,
            Cancel);

        Assert.Equal([2], narrowed.Items.Select(match => match.EventId.Value));
        Assert.Equal([1, 2], whole.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task AProgrammeThatHasMovedToTheArchiveIsStillFoundByItsKeyword()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"紀行{network}", "むかしの放送")],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}"),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.True(found.Items[0].IsArchived);
        Assert.Null(found.Items[0].Revision);
        Assert.Null(found.Items[0].Source);
    }

    [Fact]
    public async Task HowManySoundsAProgrammeAnnouncedSurvivesTheSearchAndAnArchivedOneCountsNone()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"紀行{network}", startsAt: At.AddHours(-3))],
            Cancel);
        await ProgrammesIn(scope).AddAsync(
            HeldFrom(network, 2, $"紀行{network}", string.Empty, At.AddHours(-2), sounds: 2),
            Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}"),
            At,
            Cancel);

        Assert.Equal([0, 2], found.Items.Select(match => match.Sounds));
    }

    [Fact]
    public async Task OnePageCarriesBothLayersInOneOrder()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", startsAt: At.AddHours(-3)),
                Archived(network, 3, $"紀行{network}", startsAt: At.AddHours(-1)),
            ],
            Cancel);
        await programmes.AddAsync(HeldFrom(network, 2, $"紀行{network}", string.Empty, At.AddHours(-2)), Cancel);
        await programmes.AddAsync(HeldFrom(network, 4, $"紀行{network}", string.Empty, At), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}"),
            At,
            Cancel);

        Assert.Equal(4, found.Total);
        Assert.Equal([1, 2, 3, 4], found.Items.Select(match => match.EventId.Value));
        Assert.Equal([true, false, true, false], found.Items.Select(match => match.IsArchived));
    }

    [Fact]
    public async Task APageCountsBothLayersAndSlicesAcrossTheBoundary()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", startsAt: At.AddHours(-3)),
                Archived(network, 2, $"紀行{network}", startsAt: At.AddHours(-2)),
            ],
            Cancel);
        await programmes.AddAsync(HeldFrom(network, 3, $"紀行{network}", string.Empty, At.AddHours(-1)), Cancel);
        await programmes.AddAsync(HeldFrom(network, 4, $"紀行{network}", string.Empty, At), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> second = await SearchesIn(reading).SearchAsync(
            ProgrammeSearch.For($"紀行{network}", At.AddDays(-1), At.AddDays(1), page: 2, perPage: 2)!,
            At,
            Cancel);

        Assert.Equal(4, second.Total);
        Assert.Equal([3, 4], second.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task ASpanThatEndsBeforeTheArchivedProgrammeLeavesItOut()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"紀行{network}", startsAt: At.AddDays(-20))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeSearchRepository searches = SearchesIn(reading);

        Assert.Equal(
            1,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"紀行{network}", At.AddDays(-30), At, page: 1)!,
                At,
                Cancel)).Total);
        Assert.Equal(
            0,
            (await searches.SearchAsync(
                ProgrammeSearch.For($"紀行{network}", At.AddDays(-3), At, page: 1)!,
                At,
                Cancel)).Total);
    }

    [Fact]
    public async Task AGenreNarrowsTheArchivedProgrammesToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                ArchivedUnder(network, 1, $"番組{network}", 8),
                ArchivedUnder(network, 2, $"番組{network}", 6),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"番組{network}", new ProgrammeConditions { Genres = [8] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AChannelNarrowsTheArchivedProgrammesToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"番組{network}"),
                Archived(network, 2, $"番組{network}", service: 1050),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"番組{network}", new ProgrammeConditions { Channels = [new ProgrammeService(network, 1050)] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1050, found.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task AWordToLeaveOutTakesTheArchivedProgrammeOutToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", "はじめての放送"),
                Archived(network, 2, $"紀行{network}", "再放送です"),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}", new ProgrammeConditions { Exclude = "再放送" }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task NarrowingToTheTitleReachesTheArchiveInTheSameShape()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"ニュース{network}", "きょうのできごと"),
                Archived(network, 2, "大河ドラマ", $"のちほどニュース{network}を"),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"ﾆｭｰｽ{network}", new ProgrammeConditions { Fields = [ProgrammeField.Title] }),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(1, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeHeldInBothLayersIsCarriedOnceAndTheHeldOneWins()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"紀行{network}", "写しの概要", startsAt: At)],
            Cancel);
        await programmes.AddAsync(HeldFrom(network, 1, $"紀行{network}", "番組表の概要", At), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}"),
            At,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.False(found.Items[0].IsArchived);
        Assert.Equal("番組表の概要", found.Items[0].Summary);
    }

    [Fact]
    public async Task TheSameEventNumberUsedAgainLaterKeepsBothTheHeldAndTheArchivedOne()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"紀行{network}", startsAt: At.AddDays(-10))],
            Cancel);
        await programmes.AddAsync(HeldFrom(network, 1, $"紀行{network}", string.Empty, At), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            ProgrammeSearch.For($"紀行{network}", At.AddDays(-30), At.AddDays(1))!,
            At,
            Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([true, false], found.Items.Select(match => match.IsArchived));
    }

    [Fact]
    public async Task SortingByNameOrdersAcrossBothLayers()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [Archived(network, 1, $"番組{network} B", startsAt: At.AddHours(-3))],
            Cancel);
        await programmes.AddAsync(HeldFrom(network, 2, $"番組{network} A", string.Empty, At), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            ProgrammeSearch.For($"番組{network}", At.AddDays(-1), At.AddDays(1), ProgrammeSort.Name)!,
            At,
            Cancel);

        Assert.Equal([2, 1], found.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task AWithheldServiceIsDroppedFromTheArchiveToo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                Archived(network, 1, $"紀行{network}", service: 1049),
                Archived(network, 2, $"紀行{network}", service: 1080),
                Archived(network, 3, $"紀行{network}", service: 1091),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingAMonthBack($"紀行{network}").Except([new ProgrammeService(network, 1080)]),
            At,
            Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([1, 3], found.Items.Select(match => match.EventId.Value));
        Assert.All(found.Items, match => Assert.True(match.IsArchived));
    }

    [Theory]
    [MemberData(nameof(WhatIsAsked))]
    public async Task TheStoreAndTheCodeAnswerTheSameSearchTheSameWay(string named)
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            scope,
            network);
        ProgrammeSearch search = Asked[named](network);

        PaginatedList<ProgrammeMatch> stored = await SearchedAsync(search);
        PaginatedList<ProgrammeMatch> read = ProgrammeSearchMatching.Search(
            ProgrammeSearchMatching.Layered(held, kept),
            search,
            At);

        Assert.Equal(stored.Total, read.Total);
        Assert.Equal(Spelt(stored), Spelt(read));
    }

    [Fact]
    public async Task TheHourSpelledEitherWidthIsTheSameHourToBothArms()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            scope,
            network);
        ProgrammeSearch search = Ask(network, "100017時");

        PaginatedList<ProgrammeMatch> stored = await SearchedAsync(search);
        PaginatedList<ProgrammeMatch> read = ProgrammeSearchMatching.Search(
            ProgrammeSearchMatching.Layered(held, kept),
            search,
            At);

        Assert.Equal(stored.Total, read.Total);
        Assert.Equal(Spelt(stored), Spelt(read));
        Assert.Contains("ﾆｭｰｽ100017時", Spelt(read));
        Assert.Contains("ニュース10001７時", Spelt(read));
    }

    [Fact]
    public async Task TheStoreTakesTheProgrammesByTheMarksTheyCarryRatherThanByTheLettersTheyFoldInto()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await BroadcastAsync(scope, network);

        PaginatedList<ProgrammeMatch> brandNew = await SearchedAsync(Asked["a mark asked for"](network));
        PaginatedList<ProgrammeMatch> notAgain = await SearchedAsync(Asked["a mark asked for and another left out"](network));
        PaginatedList<ProgrammeMatch> both = await SearchedAsync(Asked["two marks both of which are needed"](network));
        PaginatedList<ProgrammeMatch> archived = await SearchedAsync(Asked["a mark in the archive"](network));

        Assert.Equal(["\U0001F21Fアニメ", "\U0001F21F\U0001F21Eアニメ"], Spelt(brandNew).Order(StringComparer.Ordinal));
        Assert.Equal(["\U0001F21Fアニメ"], Spelt(notAgain));
        Assert.Equal(["\U0001F21F\U0001F21Eアニメ"], Spelt(both));
        Assert.Equal(["\U0001F221ドラマ"], Spelt(archived));
    }

    [Fact]
    public async Task TheSearchesAskedHereTellTheProgrammesApartRatherThanAllAnsweringTheSame()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            scope,
            network);
        IReadOnlyList<ProgrammeMatch> layered = ProgrammeSearchMatching.Layered(held, kept);
        var answers = new HashSet<string>(StringComparer.Ordinal);

        foreach (string named in Asked.Keys)
        {
            PaginatedList<ProgrammeMatch> stored = await SearchedAsync(Asked[named](network));

            answers.Add(string.Join('|', Spelt(stored)));
            Assert.Equal(
                string.Join('|', Spelt(stored)),
                string.Join('|', Spelt(ProgrammeSearchMatching.Search(layered, Asked[named](network), At))));
        }

        Assert.Contains(string.Empty, answers);
        Assert.True(answers.Count >= 12, $"the searches asked here gave only {answers.Count} different answers");
        Assert.Equal(Asked.Count, WhatIsAsked().Count);
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanLeavesOutWhatHasFinishedBroadcasting()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
            AskingWithNoSpan($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task WhatIsOnTheAirAtThisVeryInstantIsStillToCome()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddMinutes(-10), Now.AddMinutes(20)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
            AskingWithNoSpan($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsNeverOver()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-5), null), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
            AskingWithNoSpan($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeThatEndsAtThisVeryInstantIsOverAndOneMomentLaterIsNot()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-1), Now), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(-1), Now.AddMicroseconds(1)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
            AskingWithNoSpan($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task ASpanThatReachesBackKeepsWhatHasFinishedBroadcasting()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
            ProgrammeSearch.For($"報道{network}", Now.AddDays(-1), Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(2, found.Total);
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanDoesNotReadTheArchiveWhateverTheTimesInItSay()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            AskingWithNoSpan($"報道{network}"),
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task ASpanThatOnlyLooksAheadDoesNotReadTheArchiveEither()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            ProgrammeSearch.For($"報道{network}", Now.AddHours(1), Now.AddDays(5))!,
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task ASpanThatReachesBackReadsTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(-2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeSearchRepository searches = SearchesIn(reading);

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
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddHours(2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            ProgrammeSearch.For($"報道{network}", null, Now.AddDays(1))!,
            Now,
            Cancel);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AnEndOnItsOwnRunsFromNowRatherThanFromTheStartOfTheRecord()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await programmes.AddAsync(Ran(network, 3, $"報道{network}", Now.AddDays(2), Now.AddDays(2).AddMinutes(30)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await SearchesIn(scope).SearchAsync(
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IProgrammeSearchRepository searches = SearchesIn(reading);

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
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await ArchiveIn(scope).KeepAsync(
            [KeptForHalfAnHour(network, 1, $"報道{network}", Now.AddDays(2))],
            Cancel);
        await programmes.AddAsync(Ran(network, 2, $"報道{network}", Now.AddHours(-3), Now.AddHours(-2)), Cancel);
        await programmes.AddAsync(Ran(network, 3, $"報道{network}", Now.AddHours(2), Now.AddHours(3)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
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

    [Fact]
    public async Task AProgrammeThatEndsWhereTheSpanBeginsIsOutsideItAndOneMomentLaterIsInside()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddHours(-1), From), Cancel);
        await programmes.AddAsync(
            Ran(network, 2, $"報道{network}", From.AddHours(-1), From.AddMicroseconds(1)),
            Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeThatBeginsWhereTheSpanEndsIsOutsideItAndOneMomentEarlierIsInside()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To, To.AddHours(1)), Cancel);
        await programmes.AddAsync(
            Ran(network, 2, $"報道{network}", To.AddMicroseconds(-1), To.AddHours(1)),
            Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(1, found.Total);
        Assert.Equal(2, found.Items[0].EventId.Value);
    }

    [Fact]
    public async Task AProgrammeThatFinishedAnHourBeforeTheSpanBeganIsNotDraggedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddHours(-2), From.AddHours(-1)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AProgrammeThatBeginsAnHourAfterTheSpanEndedIsNotDraggedIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To.AddHours(1), To.AddHours(2)), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsInsideASpanItBeganBefore()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", From.AddDays(-1), null), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsNotKnownYetIsStillOutsideASpanItBeginsAfter()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IProgrammeRepository programmes = ProgrammesIn(scope);

        await programmes.AddAsync(Ran(network, 1, $"報道{network}", To.AddHours(1), null), Cancel);
        await ContextIn(scope).SaveChangesAsync(Cancel);

        PaginatedList<ProgrammeMatch> found = await Searching(scope, network);

        Assert.Equal(0, found.Total);
    }

    [Fact]
    public async Task AnArchivedProgrammeIsHeldToTheSameSpanEndsAsAHeldOne()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync(
            [
                Kept(network, 1, $"報道{network}", From.AddHours(-1), From),
                Kept(network, 2, $"報道{network}", From.AddHours(-1), From.AddMicroseconds(1)),
                Kept(network, 3, $"報道{network}", To, To.AddHours(1)),
                Kept(network, 4, $"報道{network}", To.AddMicroseconds(-1), To.AddHours(1)),
            ],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await Searching(reading, network);

        Assert.Equal([2, 4], found.Items.Select(match => match.EventId.Value));
    }

    [Fact]
    public async Task ASearchLeavesOutTheScheduleAReplacementHeardLaterCovers()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAsync(
            container,
            ReplacedSchedule.On(network, SupersededProgrammes.Service, SupersededProgrammes.Start),
            []);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> found = await SearchesIn(reading).SearchAsync(
            SupersededProgrammes.Everything(network),
            SupersededProgrammes.Start.AddHours(-3),
            Cancel);

        Assert.Equal(ReplacedSchedule.Standing.Count, found.Total);
        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], found.Items.Select(match => match.Name));
    }

    [Fact]
    public async Task TheOldScheduleStaysLeftOutOfThePastDayAfterItAndItsReplacementMoveToTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAsync(
            container,
            [],
            [.. ReplacedSchedule.On(network, SupersededProgrammes.Service, SupersededProgrammes.Start).Select(SupersededProgrammes.Moved)]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);

        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], SupersededProgrammes.Names(history));
        Assert.Equal(3, history.Total);
    }

    [Fact]
    public async Task AnOldScheduleKeptBeforeHeardTimesWereKeptIsLeftOutBesideAReplacementThatHasOne()
    {
        int network = BroadcastIds.NextNetwork();
        int stillInTheGuide = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAnOldScheduleBesideAReplacementAsync(container, network, stillInTheGuide);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);
        PaginatedList<ProgrammeMatch> historyBesideTheGuide = await HistoryAsync(reading, stillInTheGuide);

        Assert.Equal(["前の番組", "差し替えの番組", "次の番組"], SupersededProgrammes.Names(history));
        Assert.Equal(["差し替えの番組"], SupersededProgrammes.Names(historyBesideTheGuide));
    }

    [Fact]
    public async Task TwoKeptProgrammesWithoutAHeardTimeBothStay()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepTwoProgrammesWithoutAHeardTimeAsync(container, network);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        PaginatedList<ProgrammeMatch> history = await HistoryAsync(reading, network);

        Assert.Equal(2, history.Total);
    }

    [Fact]
    public async Task TheStoreLeavesOutTheSameProgrammesAsTheCode()
    {
        int network = BroadcastIds.NextNetwork();
        Programme[] held = SupersededProgrammes.Varied(network);
        ArchivedProgramme[] kept = SupersededProgrammes.Kept(network);
        await SupersededProgrammes.KeepAsync(container, held, kept);
        var code = new HeldProgrammes();
        code.Programmes.AddRange(held);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        var codeArchive = new HeldArchive(code);
        codeArchive.Programmes.AddRange(kept);
        ProgrammeSearch search = SupersededProgrammes.Everything(network);
        PaginatedList<ProgrammeMatch> stored = await SearchesIn(reading)
            .SearchAsync(search, SupersededProgrammes.Start, Cancel);
        PaginatedList<ProgrammeMatch> read = await new HeldSearches(code, codeArchive)
            .SearchAsync(search, SupersededProgrammes.Start, Cancel);

        Assert.Equal(read.Total, stored.Total);
        Assert.Equal(SupersededProgrammes.Names(read), SupersededProgrammes.Names(stored));
        Assert.Contains(stored.Items, match => match.IsArchived);
        Assert.Equal(SupersededProgrammes.Names(stored), await PageByPageAsync(SearchesIn(reading), network));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    private static Programme Beginning(int network, int carried, DateTime began)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(1),
            began,
            began.AddMinutes(30),
            $"day{carried}",
            string.Empty,
            false));

    private static Programme Under(int network, int carried, string name, ProgrammeGenre genre)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(1),
            At.AddMinutes(carried),
            At.AddMinutes(carried + 30),
            name,
            string.Empty,
            false)
        {
            Genres = [genre],
        });

    private static Programme Skeleton(int network, int carried, string name)
        => Held(new ProgrammeBroadcast(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(1),
            At.AddMinutes(carried),
            At.AddMinutes(carried + 30),
            name,
            string.Empty,
            true));

    private static ProgrammeSearch Asking(string keyword, ProgrammeConditions? conditions = null)
        => ProgrammeSearch.For(keyword, At.AddHours(-1), At.AddDays(1), conditions: conditions)!;

    private static Programme Described(int network, int carried, string name, string summary)
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
        => Programme.Discover(broadcast, At);

    private static IProgrammeRepository ProgrammesIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();

    private static IProgrammeSearchRepository SearchesIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeSearchRepository>();

    private static CarinaDbContext ContextIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static ProgrammeSearch AskingAMonthBack(string keyword, ProgrammeConditions? conditions = null)
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

    private static ArchivedProgramme ArchivedUnder(int network, int carried, string name, params int[] genres)
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

    private static Programme HeldFrom(
        int network,
        int carried,
        string name,
        string summary,
        DateTime began,
        int sounds = 0)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(1),
                began,
                began.AddMinutes(30),
                name,
                summary,
                false)
            {
                Sounds = sounds,
            },
            At);

    private static IArchivedProgrammeRepository ArchiveIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IArchivedProgrammeRepository>();

    private async Task<PaginatedList<ProgrammeMatch>> SearchedAsync(ProgrammeSearch search)
    {
        var guideLeavesOut = new HeldServices();

        guideLeavesOut.Services.AddRange(search.Withheld.Select(service => BroadcastService.Discover(
            new NetworkId(service.NetworkId),
            new ServiceId(service.ServiceId),
            "data",
            ServiceCategory.Data,
            At)));

        await using ServiceProvider screened = database.Infrastructure(replace =>
        {
            replace.AddSingleton<IBroadcastStreamDirectory>(new HeldStreams([]));
            replace.AddSingleton<IBroadcastServiceRepository>(guideLeavesOut);
        });
        await using AsyncServiceScope scope = screened.CreateAsyncScope();
        ProgrammeSearch bound = await scope.ServiceProvider
            .GetRequiredService<ProgrammeSearchScopeDomainService>()
            .BoundAsync(search, Cancel);

        return await SearchesIn(scope).SearchAsync(bound, At, Cancel);
    }

    private static string[] Spelt(PaginatedList<ProgrammeMatch> found)
        => [.. found.Items.Select(match => match.Name)];

    private static ProgrammeSearch Ask(
        int network,
        string keyword,
        ProgrammeConditions? conditions = null,
        DateTime? from = null,
        DateTime? to = null,
        ProgrammeSort sort = ProgrammeSort.StartsAt,
        bool descending = false,
        int? page = null,
        int? perPage = null)
        => ProgrammeSearch.For(
            keyword,
            from ?? At.AddDays(-5),
            to ?? At.AddDays(1),
            sort,
            descending,
            page,
            perPage,
            (conditions ?? new ProgrammeConditions()) with
            {
                Channels = conditions?.Channels ?? Everywhere(network),
            })!;

    private static IReadOnlyList<ProgrammeService> Everywhere(int network)
        => [new ProgrammeService(network, Listed), new ProgrammeService(network, Withheld)];

    private async Task<(IReadOnlyList<Programme> Held, IReadOnlyList<ArchivedProgramme> Kept)> BroadcastAsync(
        AsyncServiceScope scope,
        int network)
    {
        Programme[] held =
        [
            HeldOnService(network, Listed, 1, "ﾆｭｰｽ100017時", $"ｷﾞｮｳｻﾞの作り方 n{network}", [new ProgrammeGenre(8, 0)]),
            HeldOnService(network, Listed, 2, "ニュース10001７時", $"ギョウザの作り方 n{network}", [new ProgrammeGenre(8, 1)]),
            HeldOnService(network, Listed, 3, "ＮＥＷＳ ｽﾍﾟｼｬﾙ", $"n{network} special", [new ProgrammeGenre(6, 0)]),
            HeldOnService(network, Listed, 4, "夏の絶景", $"n{network} 紀行", []),
            HeldOnService(network, Listed, 5, "夏の思い出", $"n{network} 絶景ではない", []),
            HeldOnService(network, Listed, 6, "100%の夏", $"n{network} ＿underscore＿", []),
            HeldOnService(network, Listed, 7, "大河ドラマ", $"n{network} 続きはニュース100017時の後で", []),
            Shadow(network, Listed, 8, "ｼｬﾄﾞｳ", $"n{network} ニュース"),
            HeldOnService(network, Listed, 9, "終", $"̈おわり n{network}", []),
            HeldOnService(network, Listed, 10, "𠮟る", $"n{network}", []),
            HeldOnService(network, Listed, 11, "", $"n{network}", []),
            HeldOnService(network, Withheld, 12, "別の局", $"n{network} ニュース", []),
            HeldOnService(network, Listed, 13, "夏%割引", $"n{network}", []),
            HeldOnService(network, Listed, 14, "夏\\の記号", $"n{network}", []),
            HeldOnService(network, Listed, 15, "パスタ", $"パスタの作り方 n{network}", []),
            HeldOnService(network, Listed, 16, "ギョウザ入門", $"n{network} 作り方はこちら", []),
            RanOnService(network, Listed, 17, "ちょうど終わる", $"n{network}", At.AddMinutes(-30), At),
            RanOnService(network, Listed, 18, "まだ続く", $"n{network}", At.AddMinutes(-30), At.AddMinutes(30)),
            RanOnService(
                network,
                Listed,
                19,
                "日をまたいで始まる",
                $"n{network}",
                At.AddHours(4).AddMinutes(30),
                At.AddHours(5)),
            HeldOnService(network, Listed, 22, "\U0001F21Fアニメ", $"n{network}", []),
            HeldOnService(network, Listed, 23, "\U0001F21F\U0001F21Eアニメ", $"n{network}", []),
            HeldOnService(network, Listed, 24, "アニメ", $"\U0001F21E n{network}", []),
            HeldOnService(network, Listed, 25, "新番組 [新]", $"再会 n{network}", []),
        ];
        ArchivedProgramme[] kept =
        [
            KeptOnService(network, Listed, 20, "ニュース100017時 再放送", $"n{network}", At.AddDays(-3)),
            KeptOnService(network, Listed, 21, "夏の絶景 再放送", $"n{network}", At.AddDays(-2)),
            KeptOnService(network, Listed, 1, "この名前は表に出ない", $"n{network}", At),
            KeptOnService(network, Listed, 26, "\U0001F221ドラマ", $"n{network}", At.AddDays(-1)),
        ];
        IProgrammeRepository programmes = ProgrammesIn(scope);

        foreach (Programme programme in held)
        {
            await programmes.AddAsync(programme, Cancel);
        }

        await ContextIn(scope).SaveChangesAsync(Cancel);
        await ArchiveIn(scope).KeepAsync(kept, Cancel);

        return (held, kept);
    }

    private static Programme HeldOnService(
        int network,
        int service,
        int carried,
        string name,
        string summary,
        IReadOnlyList<ProgrammeGenre> genres)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                summary,
                false)
            {
                Genres = genres,
            },
            At);

    private static Programme RanOnService(
        int network,
        int service,
        int carried,
        string name,
        string summary,
        DateTime began,
        DateTime ended)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                began,
                ended,
                name,
                summary,
                false),
            At);

    private static Programme Shadow(int network, int service, int carried, string name, string summary)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                summary,
                true),
            At);

    private static ArchivedProgramme KeptOnService(
        int network,
        int service,
        int carried,
        string name,
        string summary,
        DateTime began)
        => ArchivedProgramme.Rehydrate(
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

    private static ProgrammeSearch AskingWithNoSpan(string keyword)
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

    private static ArchivedProgramme KeptForHalfAnHour(int network, int carried, string name, DateTime began)
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

    private static Task<PaginatedList<ProgrammeMatch>> Searching(AsyncServiceScope scope, int network)
        => SearchesIn(scope).SearchAsync(
            ProgrammeSearch.For($"報道{network}", From, To)!,
            Now,
            Cancel);

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

    private static async Task<PaginatedList<ProgrammeMatch>> HistoryAsync(AsyncServiceScope reading, int network)
        => await SearchesIn(reading).SearchAsync(
            SupersededProgrammes.Everything(network),
            SupersededProgrammes.Start.AddDays(2),
            Cancel);

    private static async Task<string[]> PageByPageAsync(IProgrammeSearchRepository searches, int network)
    {
        List<string> read = [];

        for (int page = 1; page <= 20; page++)
        {
            ProgrammeSearch asking = ProgrammeSearch.For(
                null,
                SupersededProgrammes.Start.AddDays(-3),
                SupersededProgrammes.Start.AddDays(3),
                page: page,
                perPage: 4,
                conditions: new ProgrammeConditions
                {
                    Channels =
                    [
                        new ProgrammeService(network, SupersededProgrammes.Service),
                        new ProgrammeService(network, SupersededProgrammes.Neighbour),
                    ],
                })!;
            PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(asking, SupersededProgrammes.Start, Cancel);

            read.AddRange(SupersededProgrammes.Names(found));

            if (found.Items.Count < 4)
            {
                break;
            }
        }

        return [.. read];
    }
}
