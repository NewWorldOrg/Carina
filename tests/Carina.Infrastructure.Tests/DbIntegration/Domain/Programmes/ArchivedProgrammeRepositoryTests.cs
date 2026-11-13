using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Programmes;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ArchivedProgrammeRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    [Fact]
    public async Task WhatWasKeptComesBackForTheWindowItRanIn()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IArchivedProgrammeRepository repository = ArchiveIn(scope);

        Assert.Equal(1, await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> found = await ArchiveIn(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(-1),
            At.AddHours(1),
            Cancel);

        Assert.Equal("ニュース", Assert.Single(found).Name);
    }

    [Fact]
    public async Task WhatWasKeptComesBackWithTheTimeItWasLastHeard()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IArchivedProgrammeRepository repository = ArchiveIn(scope);

        await repository.KeepAsync(
            [Archived(network, 1, "ニュース", heard: At.AddHours(-2)), Archived(network, 2, "天気", startsAt: At.AddHours(1))],
            Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> found = await ArchiveIn(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddHours(-1),
            At.AddHours(2),
            Cancel);

        Assert.Equal([At.AddHours(-2), null], found.Select(programme => programme.LastHeardAt));
    }

    [Fact]
    public async Task AProgrammeOutsideTheWindowIsNotCarried()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await ArchiveIn(scope).KeepAsync([Archived(network, 1, "ニュース")], Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Empty(await ArchiveIn(reading).ListAsync(
            [new ProgrammeService(network, 1049)],
            At.AddDays(2),
            At.AddDays(3),
            Cancel));
    }

    [Fact]
    public async Task KeepingTheSameProgrammeAgainKeepsTheFullerOfTheTwo()
    {
        int network = BroadcastIds.NextNetwork();
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IArchivedProgrammeRepository repository = ArchiveIn(scope);

        await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel);
        await repository.KeepAsync([Archived(network, 1, "ニュース7 首都圏")], Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> found = await ArchiveIn(reading).ListAsync(
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IArchivedProgrammeRepository repository = ArchiveIn(scope);

        await repository.KeepAsync([Archived(network, 1, "ニュース")], Cancel);
        await repository.KeepAsync([Archived(network, 1, "再放送", startsAt: At.AddDays(1))], Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> found = await ArchiveIn(reading).ListAsync(
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IArchivedProgrammeRepository repository = ArchiveIn(scope);

        await repository.KeepAsync(
            [Archived(network, 1, "ニュース"), Archived(network, 2, "天気", service: 1050)],
            Cancel);

        Assert.Equal(1, await repository.ForgetServiceAsync(new NetworkId(network), new ServiceId(1049), Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Single(await ArchiveIn(reading).ListAsync(
            [new ProgrammeService(network, 1049), new ProgrammeService(network, 1050)],
            At.AddHours(-1),
            At.AddHours(1),
            Cancel));
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
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);

        Assert.Equal(ReplacedSchedule.Standing, SupersededProgrammes.Events(pastDay));
    }

    [Fact]
    public async Task AnOldScheduleKeptBeforeHeardTimesWereKeptIsLeftOutBesideAReplacementThatHasOne()
    {
        int network = BroadcastIds.NextNetwork();
        int stillInTheGuide = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepAnOldScheduleBesideAReplacementAsync(container, network, stillInTheGuide);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);
        IReadOnlyList<ArchivedProgramme> besideTheGuide = await PastDayAsync(reading, stillInTheGuide);

        Assert.Equal(ReplacedSchedule.Standing, SupersededProgrammes.Events(pastDay));
        Assert.Empty(besideTheGuide);
    }

    [Fact]
    public async Task TwoKeptProgrammesWithoutAHeardTimeBothStay()
    {
        int network = BroadcastIds.NextNetwork();
        await SupersededProgrammes.KeepTwoProgrammesWithoutAHeardTimeAsync(container, network);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<ArchivedProgramme> pastDay = await PastDayAsync(reading, network);

        Assert.Equal([1, 2], SupersededProgrammes.Events(pastDay));
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
        ProgrammeService[] both =
        [
            new ProgrammeService(network, SupersededProgrammes.Service),
            new ProgrammeService(network, SupersededProgrammes.Neighbour),
        ];

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IArchivedProgrammeRepository storeArchive = ArchiveIn(reading);
        var codeArchive = new HeldArchive(code);
        codeArchive.Programmes.AddRange(kept);
        IReadOnlyList<ArchivedProgramme> pastDays = await storeArchive.ListAsync(
            both,
            SupersededProgrammes.Start.AddDays(-3),
            SupersededProgrammes.Start,
            Cancel);

        Assert.Equal([41, 43, 44, 46, 47, 48, 49, 50, 51, 52], SupersededProgrammes.Events(pastDays));
        Assert.Equal(
            SupersededProgrammes.Faces(await codeArchive.ListAsync(
                both,
                SupersededProgrammes.Start.AddDays(-3),
                SupersededProgrammes.Start,
                Cancel)),
            SupersededProgrammes.Faces(pastDays));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    private static ArchivedProgramme Archived(
        int network,
        int carried,
        string name,
        int service = 1049,
        DateTime? startsAt = null,
        DateTime? heard = null)
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
            At,
            heard);
    }

    private static IArchivedProgrammeRepository ArchiveIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IArchivedProgrammeRepository>();

    private static async Task<IReadOnlyList<ArchivedProgramme>> PastDayAsync(AsyncServiceScope reading, int network)
        => await ArchiveIn(reading).ListAsync(
            [new ProgrammeService(network, SupersededProgrammes.Service)],
            SupersededProgrammes.Start.AddHours(-2),
            SupersededProgrammes.Start.AddHours(6),
            Cancel);
}
