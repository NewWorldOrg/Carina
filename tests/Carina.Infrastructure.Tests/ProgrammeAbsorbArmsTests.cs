using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeAbsorbArmsTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = At.AddHours(1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private sealed record Visit(Func<int, ProgrammeBroadcast[]> Held, Func<int, ProgrammeBroadcast[]> Arriving);

    public static TheoryData<string> WhatArrives()
    {
        var carried = new TheoryData<string>();

        foreach (string named in Visits.Keys)
        {
            carried.Add(named);
        }

        return carried;
    }

    private static readonly Dictionary<string, Visit> Visits = new(StringComparer.Ordinal)
    {
        ["a programme nobody held yet"] = new(_ => [], network => [Broadcast(network)]),
        ["the same broadcast again"] = new(network => [Broadcast(network)], network => [Broadcast(network)]),
        ["a name that arrives empty"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = string.Empty }]),
        ["a summary that arrives empty"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Summary = string.Empty }]),
        ["a name that arrives empty for a programme whose name was already empty"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }]),
        ["a name for a programme that had none"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network)]),
        ["an end not yet known"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = null }]),
        ["an end arriving for one that was open"] = new(
            network => [Broadcast(network) with { EndsAt = null }],
            network => [Broadcast(network)]),
        ["an end that is not after the start"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = At.AddHours(22) }]),
        ["an end that is not after the start, for one nobody held"] = new(
            _ => [],
            network => [Broadcast(network) with { EndsAt = At.AddHours(22) }]),
        ["a start moved past the end, with no end of its own"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(23), EndsAt = null }]),
        ["a start moved past the end, ending where it starts"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(23), EndsAt = At.AddHours(23) }]),
        ["a start that moved but not past the end"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { StartsAt = At.AddHours(22.5), EndsAt = null }]),
        ["a later end"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { EndsAt = At.AddHours(24) }]),
        ["genres that arrive empty"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15)] }],
            network => [Broadcast(network)]),
        ["genres that arrive different"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15)] }],
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(7, 2), new ProgrammeGenre(0, 15)] }]),
        ["genres that arrive in another order"] = new(
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(0, 15), new ProgrammeGenre(7, 2)] }],
            network => [Broadcast(network) with { Genres = [new ProgrammeGenre(7, 2), new ProgrammeGenre(0, 15)] }]),
        ["items that arrive empty"] = new(
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }],
            network => [Broadcast(network)]),
        ["items that arrive for one that had none"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }]),
        ["items whose text changed"] = new(
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "きょうの内容")] }],
            network => [Broadcast(network) with { Items = [new ProgrammeItem("番組内容", "あしたの内容")] }]),
        ["related programmes that arrive empty"] = new(
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Shared)] }],
            network => [Broadcast(network)]),
        ["related programmes that arrive different"] = new(
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Shared)] }],
            network => [Broadcast(network) with { Related = [new RelatedProgramme(network, 1048, 1, RelationKind.Moved)] }]),
        ["another stream"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { TransportStreamId = new TransportStreamId(32740) }]),
        ["a placeholder that became a programme"] = new(
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }],
            network => [Broadcast(network) with { IsShadow = false }]),
        ["a programme that became a placeholder"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = string.Empty, IsShadow = true }]),
        ["subtitles that appeared"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { HasSubtitles = true }]),
        ["subtitles that went away"] = new(
            network => [Broadcast(network) with { HasSubtitles = true }],
            network => [Broadcast(network)]),
        ["a source that changed"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Source = ProgrammeSource.PresentFollowing }]),
        ["a name longer than the column"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength + 40) }]),
        ["a name exactly as long as the column, already held"] = new(
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength) }],
            network => [Broadcast(network) with { Name = new string('長', Programme.NameMaxLength + 1) }]),
        ["a summary longer than the column"] = new(
            _ => [],
            network => [Broadcast(network) with { Summary = new string('概', Programme.SummaryMaxLength + 1) }]),
        ["a name with a character outside the basic plane"] = new(
            network => [Broadcast(network)],
            network => [Broadcast(network) with { Name = "\U0001F211字幕つき\U0001F214" }]),
        ["several programmes, some new, some changed, some the same"] = new(
            network => [Broadcast(network, 1), Broadcast(network, 2), Broadcast(network, 3) with { EndsAt = null }],
            network =>
            [
                Broadcast(network, 1),
                Broadcast(network, 2) with { Name = "夜のニュース" },
                Broadcast(network, 3),
                Broadcast(network, 4),
                Broadcast(network, 5) with { Name = string.Empty, IsShadow = true },
            ]),
    };

    [Theory]
    [MemberData(nameof(WhatArrives))]
    public async Task BrEd002TheStoreAndTheCodeAbsorbTheSameVisitTheSameWay(string named)
    {
        int network = BroadcastIds.NextNetwork();
        Visit visit = Visits[named];
        await using CarinaDbContext context = database.Open();
        var stored = new ProgrammeRepository(context);
        var read = new HeldProgrammes();

        foreach (ProgrammeBroadcast held in visit.Held(network))
        {
            await stored.AddAsync(Programme.Discover(held, At), Cancel);
        }

        await read.AbsorbAsync(visit.Held(network), At, Cancel);
        Dictionary<ProgrammeId, long> storedBefore = await RevisionsAsync(network);
        Dictionary<ProgrammeId, long> readBefore = Revisions(read);

        ProgrammesAbsorbed byTheStore = await stored.AbsorbAsync(visit.Arriving(network), Later, Cancel);
        ProgrammesAbsorbed byTheCode = await read.AbsorbAsync(visit.Arriving(network), Later, Cancel);

        Assert.Equal(byTheCode, byTheStore);

        await using CarinaDbContext reading = database.Open();
        List<Programme> fromTheStore = await ListedAsync(reading, network);

        Assert.Equal(Spelt(read.Programmes), Spelt(fromTheStore));
        Assert.Equal(Moved(read.Programmes, readBefore), Moved(fromTheStore, storedBefore));
    }

    private static List<string> Spelt(IEnumerable<Programme> programmes)
        =>
        [
            .. programmes
                .OrderBy(programme => programme.EventId.Value)
                .Select(programme => string.Join(
                    "|",
                    programme.EventId.Value,
                    programme.TransportStreamId.Value,
                    programme.StartsAt.ToString("O"),
                    programme.EndsAt?.ToString("O") ?? "open",
                    programme.Name,
                    programme.Summary,
                    programme.IsShadow,
                    string.Join(",", programme.Genres),
                    string.Join(",", programme.Items),
                    string.Join(",", programme.Related),
                    programme.HasSubtitles,
                    programme.Source,
                    programme.UpdatedAt.ToString("O"))),
        ];

    private static List<(int Event, string Revision)> Moved(
        IEnumerable<Programme> programmes,
        Dictionary<ProgrammeId, long> before)
        =>
        [
            .. programmes
                .OrderBy(programme => programme.EventId.Value)
                .Select(programme => (
                    programme.EventId.Value,
                    before.TryGetValue(programme.Id, out long was)
                        ? programme.Revision == was ? "kept" : "moved"
                        : "new")),
        ];

    private static Dictionary<ProgrammeId, long> Revisions(HeldProgrammes held)
        => held.Programmes.ToDictionary(programme => programme.Id, programme => programme.Revision);

    private async Task<Dictionary<ProgrammeId, long>> RevisionsAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();

        return (await ListedAsync(reading, network)).ToDictionary(programme => programme.Id, programme => programme.Revision);
    }

    private static async Task<List<Programme>> ListedAsync(CarinaDbContext context, int network)
        =>
        [
            .. await new ProgrammeRepository(context).ListAsync(
                new ProgrammeWindow(network, 1049, At.AddDays(-2), At.AddDays(2)),
                Cancel),
        ];

    private static ProgrammeBroadcast Broadcast(int network, int carried = 1)
        => new(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(32739),
            At.AddHours(22),
            At.AddHours(23),
            "トップニュース先出し\U0001F211",
            "きょうのみどころ",
            IsShadow: false);
}
