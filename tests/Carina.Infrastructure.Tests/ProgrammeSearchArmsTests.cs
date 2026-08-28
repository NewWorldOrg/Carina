using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchArmsTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Listed = 1049;

    private const int Withheld = 1032;

    public static TheoryData<string> WhatIsAsked()
    {
        var carried = new TheoryData<string>();

        foreach (string named in Asked.Keys)
        {
            carried.Add(named);
        }

        return carried;
    }

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
        ["one channel"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Channels = [new ProgrammeService(network, Listed)] }),
        ["a channel that carries nothing"] = network => Ask(
            network,
            string.Empty,
            new ProgrammeConditions { Channels = [new ProgrammeService(network, 1)] }),
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

    [Theory]
    [MemberData(nameof(WhatIsAsked))]
    public async Task TheStoreAndTheCodeAnswerTheSameSearchTheSameWay(string named)
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            context,
            network);
        ProgrammeSearch search = Asked[named](network);

        PaginatedList<ProgrammeMatch> stored = await new ProgrammeSearchRepository(context)
            .SearchAsync(search, At, Cancel);
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
        await using CarinaDbContext context = database.Open();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            context,
            network);
        ProgrammeSearch search = Ask(network, "100017時");

        PaginatedList<ProgrammeMatch> stored = await new ProgrammeSearchRepository(context)
            .SearchAsync(search, At, Cancel);
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
    public async Task TheSearchesAskedHereTellTheProgrammesApartRatherThanAllAnsweringTheSame()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        (IReadOnlyList<Programme> held, IReadOnlyList<ArchivedProgramme> kept) = await BroadcastAsync(
            context,
            network);
        IReadOnlyList<ProgrammeMatch> layered = ProgrammeSearchMatching.Layered(held, kept);
        var answers = new HashSet<string>(StringComparer.Ordinal);
        var repository = new ProgrammeSearchRepository(context);

        foreach (string named in Asked.Keys)
        {
            PaginatedList<ProgrammeMatch> stored = await repository.SearchAsync(Asked[named](network), At, Cancel);

            answers.Add(string.Join('|', Spelt(stored)));
            Assert.Equal(
                string.Join('|', Spelt(stored)),
                string.Join('|', Spelt(ProgrammeSearchMatching.Search(layered, Asked[named](network), At))));
        }

        Assert.Contains(string.Empty, answers);
        Assert.True(answers.Count >= 12, $"the searches asked here gave only {answers.Count} different answers");
        Assert.Equal(Asked.Count, WhatIsAsked().Count);
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
        CarinaDbContext context,
        int network)
    {
        Programme[] held =
        [
            Held(network, Listed, 1, "ﾆｭｰｽ100017時", $"ｷﾞｮｳｻﾞの作り方 n{network}", [new ProgrammeGenre(8, 0)]),
            Held(network, Listed, 2, "ニュース10001７時", $"ギョウザの作り方 n{network}", [new ProgrammeGenre(8, 1)]),
            Held(network, Listed, 3, "ＮＥＷＳ ｽﾍﾟｼｬﾙ", $"n{network} special", [new ProgrammeGenre(6, 0)]),
            Held(network, Listed, 4, "夏の絶景", $"n{network} 紀行", []),
            Held(network, Listed, 5, "夏の思い出", $"n{network} 絶景ではない", []),
            Held(network, Listed, 6, "100%の夏", $"n{network} ＿underscore＿", []),
            Held(network, Listed, 7, "大河ドラマ", $"n{network} 続きはニュース100017時の後で", []),
            Shadow(network, Listed, 8, "ｼｬﾄﾞｳ", $"n{network} ニュース"),
            Held(network, Listed, 9, "終", $"̈おわり n{network}", []),
            Held(network, Listed, 10, "𠮟る", $"n{network}", []),
            Held(network, Listed, 11, "", $"n{network}", []),
            Held(network, Withheld, 12, "別の局", $"n{network} ニュース", []),
            Held(network, Listed, 13, "夏%割引", $"n{network}", []),
            Held(network, Listed, 14, "夏\\の記号", $"n{network}", []),
            Held(network, Listed, 15, "パスタ", $"パスタの作り方 n{network}", []),
            Held(network, Listed, 16, "ギョウザ入門", $"n{network} 作り方はこちら", []),
            Ran(network, Listed, 17, "ちょうど終わる", $"n{network}", At.AddMinutes(-30), At),
            Ran(network, Listed, 18, "まだ続く", $"n{network}", At.AddMinutes(-30), At.AddMinutes(30)),
        ];
        ArchivedProgramme[] kept =
        [
            Kept(network, Listed, 20, "ニュース100017時 再放送", $"n{network}", At.AddDays(-3)),
            Kept(network, Listed, 21, "夏の絶景 再放送", $"n{network}", At.AddDays(-2)),
            Kept(network, Listed, 1, "この名前は表に出ない", $"n{network}", At),
        ];
        var programmes = new ProgrammeRepository(context);

        foreach (Programme programme in held)
        {
            await programmes.AddAsync(programme, Cancel);
        }

        await context.SaveChangesAsync(Cancel);
        await new ArchivedProgrammeRepository(context).KeepAsync(kept, Cancel);

        return (held, kept);
    }

    private static Programme Held(
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

    private static Programme Ran(
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

    private static ArchivedProgramme Kept(
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
}
