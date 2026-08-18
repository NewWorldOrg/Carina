using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ArchivedProgrammeRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatWasKeptComesBackForTheWindowItRanIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ArchivedProgrammeRepository(context);

        Assert.Equal(1, await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel));

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> found = await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(-1),
            At.AddHours(1),
            Cancel);

        Assert.Equal("ニュース", Assert.Single(found).Name);
    }

    [Fact]
    public async Task AProgrammeOutsideTheWindowIsNotCarried()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await new ArchivedProgrammeRepository(context).KeepAsync([Archived(network, 1, "ニュース")], Cancel);

        await using CarinaDbContext reading = database.Open();

        Assert.Empty(await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddDays(2),
            At.AddDays(3),
            Cancel));
    }

    [Fact]
    public async Task KeepingTheSameProgrammeAgainKeepsTheFullerOfTheTwo()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ArchivedProgrammeRepository(context);

        await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel);
        await repository.KeepAsync([Archived(network, 1, "ニュース7 首都圏")], Cancel);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> found = await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(-1),
            At.AddHours(1),
            Cancel);

        Assert.Equal("ニュース7 首都圏", Assert.Single(found).Name);
    }

    [Fact]
    public async Task TheSameEventStartingAtAnotherTimeIsHeldBeside()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ArchivedProgrammeRepository(context);

        await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel);
        await repository.KeepAsync([Archived(network, 1, "再放送", startsAt: At.AddDays(1))], Cancel);

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<ArchivedProgramme> found = await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(-1),
            At.AddDays(2),
            Cancel);

        Assert.Equal(["ニュース", "再放送"], found.Select(programme => programme.Name));
    }

    [Fact]
    public async Task ForgettingAServiceLeavesItsNeighboursAlone()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();
        var repository = new ArchivedProgrammeRepository(context);

        await repository.KeepAsync(
            [Archived(network, 1, "ニュース"), Archived(network, 2, "天気", service: 1050)],
            Cancel);

        Assert.Equal(1, await repository.ForgetServiceAsync(new NetworkId(network), new ServiceId(1049), Cancel));

        await using CarinaDbContext reading = database.Open();

        Assert.Single(await new ArchivedProgrammeRepository(reading).ListAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(network, 1050)],
            At.AddHours(-1),
            At.AddHours(1),
            Cancel));
    }

    private static ArchivedProgramme Archived(
        int network,
        int carried,
        string name,
        int service = 1049,
        DateTime? startsAt = null)
    {
        DateTime began = startsAt ?? At;

        return ArchivedProgramme.Rehydrate(
            new NetworkId(network),
            new ServiceId(service),
            new EventId(carried),
            began,
            began.AddMinutes(30),
            name,
            string.Empty,
            false,
            [],
            [],
            At);
    }
}
