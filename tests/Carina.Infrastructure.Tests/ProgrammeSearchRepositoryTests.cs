using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
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

        await repository.AddAsync(Programme(network, 1, $"ニュース{network}", "今日のできごと"), Cancel);
        await repository.AddAsync(Programme(network, 2, "大河ドラマ", $"続きはニュース{network}の後で"), Cancel);
        await repository.AddAsync(Programme(network, 3, "天気予報", "あすの空"), Cancel);
        await context.SaveChangesAsync(Cancel);

        PaginatedList<Programme> found = await repository.SearchAsync(Asking($"ニュース{network}"), Cancel);

        Assert.Equal(2, found.Total);
        Assert.Equal([1, 2], found.Items.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task TheKeywordDoesNotCareAboutLetterCase()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);

        await repository.AddAsync(Programme(network, 1, $"Morning NEWS{network}", string.Empty), Cancel);
        await context.SaveChangesAsync(Cancel);

        Assert.Equal(1, (await repository.SearchAsync(Asking($"news{network}"), Cancel)).Total);
    }

    [Fact]
    public async Task APageCarriesOnlyItsOwnSliceAndSaysHowManyThereAre()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ProgrammeRepository(context);

        for (int carried = 1; carried <= 5; carried++)
        {
            await repository.AddAsync(Programme(network, carried, $"報道{network}の{carried}", string.Empty), Cancel);
        }

        await context.SaveChangesAsync(Cancel);

        PaginatedList<Programme> second = await repository.SearchAsync(
            ProgrammeSearch.For($"報道{network}", At.AddHours(-1), At.AddDays(1), page: 2, perPage: 2)!,
            Cancel);

        Assert.Equal(5, second.Total);
        Assert.Equal(3, second.LastPage);
        Assert.Equal(2, second.Items.Count);
    }

    private static ProgrammeSearch Asking(string keyword)
        => ProgrammeSearch.For(keyword, At.AddHours(-1), At.AddDays(1))!;

    private static Programme Programme(int network, int carried, string name, string summary)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(1),
                At.AddMinutes(carried),
                At.AddMinutes(carried + 30),
                name,
                summary,
                false),
            At);
}
