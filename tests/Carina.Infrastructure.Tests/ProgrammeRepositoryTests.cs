using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;


    [Fact]
    public async Task AProgrammeComesBackWithEverythingItWasBroadcastWith()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Broadcast(network) with
        {
            Genres = [new ProgrammeGenre(0, 15), new ProgrammeGenre(11, 5)],
            Items = [new ProgrammeItem("番組内容", "きょうの内容")],
            Related = [new RelatedProgramme(network, 1048, 47289, RelationKind.Shared)],
            HasSubtitles = true,
            Source = ProgrammeSource.PresentFollowing,
        }, At), Cancel);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network), Cancel);

        Assert.NotNull(stored);
        Assert.Equal("トップニュース先出し🈑", stored.Name);
        Assert.Equal([0, 11], stored.Genres.Select(genre => genre.Kind));
        Assert.Equal("番組内容", Assert.Single(stored.Items).Heading);
        Assert.Equal(RelationKind.Shared, Assert.Single(stored.Related).Kind);
        Assert.True(stored.HasSubtitles);
        Assert.Equal(ProgrammeSource.PresentFollowing, stored.Source);
        Assert.Equal(At.AddHours(23), stored.EndsAt);
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsStillOpenComesBackWithoutOne()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ProgrammeRepository(context).AddAsync(
            Programme.Discover(Broadcast(network) with { EndsAt = null }, At),
            Cancel);

        await using CarinaDbContext reading = database.Open();

        Assert.Null((await new ProgrammeRepository(reading).FindAsync(Id(network), Cancel))!.EndsAt);
    }

    [Fact]
    public async Task OnlyTheProgrammesTouchingTheWindowComeBack()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(20)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(20.5)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 3, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 4, At.AddHours(24)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 5, At.AddHours(30)), At), Cancel);

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<Programme> carried = await new ProgrammeRepository(reading).ListAsync(
            new ProgrammeWindow(network, 1049, At.AddHours(21), At.AddHours(24)),
            Cancel);

        Assert.Equal([2, 3], carried.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsStillOpenIsCarriedByAnyWindowAfterItStarts()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ProgrammeRepository(context).AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(22)) with { EndsAt = null }, At),
            Cancel);

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<Programme> carried = await new ProgrammeRepository(reading).ListAsync(
            new ProgrammeWindow(network, 1049, At.AddHours(23), At.AddHours(24)),
            Cancel);

        Assert.Single(carried);
    }

    [Fact]
    public async Task OnlyTheProgrammesHandedOverAreForgotten()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(1)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Broadcast(network, 2, At.AddHours(40)), At), Cancel);

        Assert.Equal(1, await programmes.ForgetAsync(await Ended(programmes, network), Cancel));

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));
        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 2), Cancel));
    }

    [Fact]
    public async Task ForgettingNothingReadsNothingAndWritesNothing()
    {
        await using CarinaDbContext context = database.Open();

        Assert.Equal(0, await new ProgrammeRepository(context).ForgetAsync([], Cancel));
    }

    [Fact]
    public async Task WhatAProgrammeTookInLaterComesBackFromTheStore()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);
        var programme = Programme.Discover(Broadcast(network), At);

        await programmes.AddAsync(programme, Cancel);

        Assert.True(programme.Absorb(
            Broadcast(network) with
            {
                Genres = [new ProgrammeGenre(7, 2)],
                Items = [new ProgrammeItem("公式ページ", "https://example.invalid/")],
                HasSubtitles = true,
            },
            At.AddHours(1)));

        await programmes.SaveAsync(programme, Cancel);

        await using CarinaDbContext reading = database.Open();
        Programme? stored = await new ProgrammeRepository(reading).FindAsync(Id(network), Cancel);

        Assert.Equal(new ProgrammeGenre(7, 2), Assert.Single(stored!.Genres));
        Assert.Equal("公式ページ", Assert.Single(stored.Items).Heading);
        Assert.True(stored.HasSubtitles);
        Assert.Equal(At.AddHours(1), stored.UpdatedAt);
    }

    [Fact]
    public async Task AProgrammeWhoseEndWasNeverToldIsNeverAmongWhatEnded()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 1, At.AddHours(1)) with { EndsAt = null }, At),
            Cancel);
        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 2, At.AddHours(40)) with { EndsAt = null }, At),
            Cancel);

        Assert.Empty(await Ended(programmes, network));

        await using CarinaDbContext reading = database.Open();

        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));
        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 2), Cancel));
    }

    [Fact]
    public async Task HowFarAServiceIsCoveredIgnoresTheProgrammesThatAreOnlyPlaceholders()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Broadcast(network, 1, At.AddHours(20)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Broadcast(network, 2, At.AddHours(40)) with { IsShadow = true }, At),
            Cancel);

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(
            At.AddHours(20),
            await new ProgrammeRepository(reading).CoveredUntilAsync(network, 1049, Cancel));
    }

    [Fact]
    public async Task AServiceWithNoProgrammesIsCoveredUntilNoTimeAtAll()
    {
        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new ProgrammeRepository(reading).CoveredUntilAsync(NextNetwork(), 1049, Cancel));
    }

    [Fact]
    public async Task TheProgrammesOfEveryAskedServiceComeBackTogether()
    {
        int network = NextNetwork();
        int neighbour = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(neighbour, 1050, 1, At.AddHours(22.5)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 2, At.AddHours(30)), At), Cancel);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<Programme> found = await new ProgrammeRepository(reading).ListForServicesAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(neighbour, 1050)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.Equal(
            [(network, 1049, 1), (neighbour, 1050, 1)],
            found.Select(programme
                => (programme.NetworkId.Value, programme.ServiceId.Value, programme.EventId.Value)));
    }

    [Fact]
    public async Task AProgrammeOfANetworkOrServiceNotAskedForIsNotCarried()
    {
        int network = NextNetwork();
        int neighbour = NextNetwork();
        int stranger = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1050, 2, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(network, 1048, 3, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(Programme.Discover(Carried(stranger, 1049, 4, At.AddHours(22)), At), Cancel);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<Programme> found = await new ProgrammeRepository(reading).ListForServicesAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(neighbour, 1050)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.DoesNotContain(found, programme => programme.NetworkId.Value == stranger);
        Assert.DoesNotContain(found, programme => programme.ServiceId.Value == 1048);
        Assert.DoesNotContain(
            found,
            programme => programme.NetworkId.Value == network && programme.ServiceId.Value == 1050);
        Assert.Equal(1, Assert.Single(found).EventId.Value);
    }

    [Fact]
    public async Task ASkeletonEventIsHeldButNeverHandedToTheGuide()
    {
        int network = NextNetwork();
        await using CarinaDbContext context = database.Open();
        var programmes = new ProgrammeRepository(context);

        await programmes.AddAsync(Programme.Discover(Carried(network, 1049, 1, At.AddHours(22)), At), Cancel);
        await programmes.AddAsync(
            Programme.Discover(Carried(network, 1049, 2, At.AddHours(22)) with { IsShadow = true }, At),
            Cancel);

        await using CarinaDbContext reading = database.Open();
        var asked = new ProgrammeRepository(reading);
        IReadOnlyList<Programme> found = await asked.ListForServicesAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(21),
            At.AddHours(24),
            Cancel);

        Assert.Equal(1, Assert.Single(found).EventId.Value);
        Assert.NotNull(await asked.FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(2)),
            Cancel));
    }

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    private static ProgrammeId Id(int network, int carried = 1)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private static async Task<Programme[]> Ended(ProgrammeRepository programmes, int network)
        => [.. (await programmes.ListEndedBeforeAsync(At.AddHours(10), 5_000, Cancel))
            .Where(programme => programme.NetworkId.Value == network)];

    private static ProgrammeBroadcast Broadcast(int network, int carried = 1, DateTime? startsAt = null)
        => new(
            Id(network, carried),
            new TransportStreamId(32739),
            startsAt ?? At.AddHours(22),
            (startsAt ?? At.AddHours(22)).AddHours(1),
            "トップニュース先出し\U0001F211",
            "きょうのみどころ",
            IsShadow: false);

    private static ProgrammeBroadcast Carried(int network, int service, int carried, DateTime startsAt)
        => new(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(32739),
            startsAt,
            startsAt.AddHours(1),
            "トップニュース先出し\U0001F211",
            "きょうのみどころ",
            IsShadow: false);
}
