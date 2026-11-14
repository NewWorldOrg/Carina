using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ArchiveTransferTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AProgrammeLongEndedIsKeptBeforeItIsLetGoOfFromTheGuide()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-3));

        Transferred moved = await TransferAsync();

        Assert.Equal(1, moved.Kept);
        Assert.Equal(1, moved.Discarded);

        Assert.Null(await FindAsync(Id(network, 1), Cancel));
        Assert.Single(await ArchivedAsync(
            [new ProgrammeService(network, 1049)],
            Now.AddDays(-4),
            Now,
            Cancel));
    }

    [Fact]
    public async Task AProgrammeThatOnlyJustEndedStaysInTheGuide()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddHours(-2));

        Transferred moved = await TransferAsync();

        Assert.Equal(0, moved.Kept);

        Assert.NotNull(await FindAsync(Id(network, 1), Cancel));
    }

    [Fact]
    public async Task AShadowIsLetGoOfWithoutBeingKept()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-3), isShadow: true);

        Transferred moved = await TransferAsync();

        Assert.Equal(0, moved.Kept);
        Assert.Equal(1, moved.Discarded);
    }

    [Fact]
    public async Task WithNoRetentionSetTheArchiveKeepsEverything()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-400));

        Assert.Equal(0, (await TransferAsync()).Forgotten);
    }

    [Fact]
    public async Task WithARetentionSetTheArchiveLetsGoOfWhatIsOlderThanIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-400));

        Transferred moved = await TransferAsync(new CollectionSettings { ArchiveRetention = TimeSpan.FromDays(365) });

        Assert.Equal(1, moved.Kept);

        Assert.Empty(await ArchivedAsync(
            [new ProgrammeService(network, 1049)],
            Now.AddDays(-401),
            Now,
            Cancel));
    }

    [Fact]
    public async Task AtTheCapEveryProgrammeLeavingTheGuideIsKept()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await AddMany(context, network, ProgrammeArchivingDomainService.MostPerRun);

        Transferred moved = await TransferAsync();

        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, moved.Kept);
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, moved.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
    }

    [Fact]
    public async Task PastTheCapWhatTheArchiveDidNotTakeStaysInTheGuide()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await AddMany(context, network, ProgrammeArchivingDomainService.MostPerRun + 1);

        Transferred first = await TransferAsync();

        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, first.Kept);
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, first.Discarded);

        await using CarinaDbContext again = database.Open();

        Assert.Equal(1, await again.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun, await again.Set<ArchivedProgramme>().CountAsync(Cancel));

        Transferred second = await TransferAsync();

        Assert.Equal(1, second.Kept);
        Assert.Equal(1, second.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ProgrammeArchivingDomainService.MostPerRun + 1, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsUndecidedIsNotLetGoOf()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-3), endIsUndecided: true);

        Transferred moved = await TransferAsync();

        Assert.Equal(0, moved.Kept);
        Assert.Equal(0, moved.Discarded);

        Assert.NotNull(await FindAsync(Id(network, 1), Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndWasNeverToldIsKeptEndingWhereTheOneAfterItBegan()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-3), endIsUndecided: true);
        await Add(network, 2, Now.AddDays(-3).AddHours(1));

        Transferred moved = await TransferAsync();

        Assert.Equal(2, moved.Kept);
        Assert.Equal(2, moved.Discarded);

        await using CarinaDbContext reading = database.Open();
        ArchivedProgramme kept = await reading.Set<ArchivedProgramme>()
            .SingleAsync(programme => programme.EventId == new EventId(1), Cancel);

        Assert.Equal(0, await reading.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(Now.AddDays(-3).AddHours(1), kept.EndsAt);
    }

    [Fact]
    public async Task WhenTheGuideCannotLetGoTheArchiveKeepsNothingEither()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(network, 1, Now.AddDays(-3));

        await using ServiceProvider container = Container(
            new CollectionSettings(),
            Now,
            replace => RegisteredServices.Wrap<IProgrammeRepository>(replace, held => new StubbornProgrammes(held)));
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        ArchiveTransfer transfer = scope.ServiceProvider.GetRequiredService<ArchiveTransfer>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transfer.RunAsync(Cancel));

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
        Assert.NotNull(await FindAsync(Id(network, 1), Cancel));

        await Empty(reading);
    }

    [Fact]
    public async Task TheOldScheduleStaysLeftOutAfterItAndThenItsReplacementMoveToTheArchive()
    {
        int network = BroadcastIds.NextNetwork();
        DateTime start = Now.AddDays(-3);
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await AddAllAsync(context, ReplacedSchedule.On(network, 1049, start));

        await TransferAsync(at: start.AddDays(1).AddMinutes(245));

        int[] firstKept = await KeptAsync(network, start);

        Assert.Equal([ReplacedSchedule.Before], firstKept);

        await TransferAsync(at: start.AddDays(1).AddMinutes(255));

        await using (CarinaDbContext reading = database.Open())
        {
            int[] kept = await KeptAsync(network, start);

            Assert.Equal([ReplacedSchedule.Before, ReplacedSchedule.Replacement], kept);
            Assert.Equal(
                ReplacedSchedule.Replaced.Count + 2,
                await reading.Set<ArchivedProgramme>().CountAsync(
                    programme => programme.NetworkId == new NetworkId(network),
                    Cancel));
        }

        await Empty(context);
    }

    [Fact]
    public async Task AnOldScheduleKeptWithoutHeardTimesIsLeftOutOnceItsReplacementMovesBesideIt()
    {
        int network = BroadcastIds.NextNetwork();
        DateTime start = Now.AddDays(-3);
        IReadOnlyList<Programme> schedule = ReplacedSchedule.On(network, 1049, start);
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await KeepUnjudgedAsync(
            context,
            schedule.Where(programme => ReplacedSchedule.Replaced.Contains(programme.EventId.Value)));
        await AddAllAsync(
            context,
            schedule.Where(programme => !ReplacedSchedule.Replaced.Contains(programme.EventId.Value)));

        int[] unjudged = await KeptAsync(network, start);

        Assert.Equal(ReplacedSchedule.Replaced, unjudged);

        await TransferAsync(at: start.AddDays(1).AddMinutes(255));

        int[] kept = await KeptAsync(network, start);

        Assert.Equal([ReplacedSchedule.Before, ReplacedSchedule.Replacement], kept);

        await Empty(context);
    }

    private async Task<int[]> KeptAsync(int network, DateTime start)
        =>
        [
            .. (await ArchivedAsync(
                    [new ProgrammeService(network, 1049)],
                    start.AddHours(-2),
                    start.AddHours(6),
                    Cancel))
                .Select(programme => programme.EventId.Value)
                .Order(),
        ];

    private static async Task AddAllAsync(CarinaDbContext context, IEnumerable<Programme> programmes)
    {
        await context.AddRangeAsync(programmes, Cancel);
        await context.SaveChangesAsync(Cancel);
    }

    private static async Task KeepUnjudgedAsync(CarinaDbContext context, IEnumerable<Programme> programmes)
    {
        foreach (Programme programme in programmes)
        {
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO archived_programme (
                    network_id, service_id, event_id, start_at, end_at, name, summary, has_subtitles, genres, items,
                    archived_at, superseded)
                VALUES (
                    {programme.NetworkId.Value}, {programme.ServiceId.Value}, {programme.EventId.Value},
                    {programme.StartsAt}, {programme.EndsAt}, {programme.Name}, {programme.Summary}, false,
                    '[]'::jsonb, '[]'::jsonb, {programme.StartsAt}, false)
                """,
                Cancel);
        }
    }

    private async Task Add(
        int network,
        int carried,
        DateTime startsAt,
        bool isShadow = false,
        bool endIsUndecided = false)
    {
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IProgrammeRepository>().AddAsync(
            Programme.Discover(
                new ProgrammeBroadcast(
                    Id(network, carried),
                    new TransportStreamId(32_736),
                    startsAt,
                    endIsUndecided ? null : startsAt.AddMinutes(30),
                    "ニュース",
                    string.Empty,
                    isShadow),
                startsAt),
            Cancel);
        await scope.ServiceProvider.GetRequiredService<CarinaDbContext>().SaveChangesAsync(Cancel);
    }

    private static async Task AddMany(CarinaDbContext context, int network, int count)
    {
        DateTime first = Now.AddDays(-10);

        for (int carried = 1; carried <= count; carried++)
        {
            DateTime startsAt = first.AddMinutes(carried);

            await context.AddAsync(
                Programme.Discover(
                    new ProgrammeBroadcast(
                        Id(network, carried),
                        new TransportStreamId(32_736),
                        startsAt,
                        startsAt.AddMinutes(30),
                        "ニュース",
                        string.Empty,
                        false),
                    startsAt),
                Cancel);
        }

        await context.SaveChangesAsync(Cancel);
    }

    private static async Task Empty(CarinaDbContext context)
    {
        await context.Set<Programme>().ExecuteDeleteAsync(Cancel);
        await context.Set<ArchivedProgramme>().ExecuteDeleteAsync(Cancel);
    }

    private static ProgrammeId Id(int network, int carried)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private async Task<Transferred> TransferAsync(CollectionSettings? settings = null, DateTime? at = null)
    {
        await using ServiceProvider container = Container(settings ?? new CollectionSettings(), at ?? Now);
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ArchiveTransfer>().RunAsync(Cancel);
    }

    private ServiceProvider Container(CollectionSettings settings, DateTime at, Action<IServiceCollection>? also = null)
        => database.Infrastructure(replace =>
        {
            replace.AddSingleton(settings);
            replace.AddSingleton<TimeProvider>(new FixedClock(at));
            also?.Invoke(replace);
        });

    private async Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
    {
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IProgrammeRepository>().FindAsync(id, cancellationToken);
    }

    private async Task<IReadOnlyList<ArchivedProgramme>> ArchivedAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IArchivedProgrammeRepository>()
            .ListAsync(services, from, to, cancellationToken);
    }

    private sealed class StubbornProgrammes(IProgrammeRepository held) : IProgrammeRepository
    {
        public Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
            => held.FindAsync(id, cancellationToken);

        public Task<Programme?> FindUnlessSupersededAsync(ProgrammeId id, CancellationToken cancellationToken)
            => held.FindUnlessSupersededAsync(id, cancellationToken);

        public Task<IReadOnlyList<Programme>> ListAsync(ProgrammeWindow window, CancellationToken cancellationToken)
            => held.ListAsync(window, cancellationToken);

        public Task<IReadOnlyList<Programme>> ListForServicesAsync(
            IReadOnlyList<ProgrammeService> services,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken)
            => held.ListForServicesAsync(services, from, to, cancellationToken);

        public Task AddAsync(Programme programme, CancellationToken cancellationToken)
            => held.AddAsync(programme, cancellationToken);

        public Task<ProgrammesAbsorbed> AbsorbAsync(
            IReadOnlyList<ProgrammeBroadcast> broadcasts,
            IReadOnlyList<ProgrammeService> heardWhole,
            DateTime at,
            CancellationToken cancellationToken)
            => held.AbsorbAsync(broadcasts, heardWhole, at, cancellationToken);

        public Task<IReadOnlyList<EndedProgramme>> ListEndedBeforeAsync(
            DateTime at,
            int rows,
            CancellationToken cancellationToken)
            => held.ListEndedBeforeAsync(at, rows, cancellationToken);

        public Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the guide would not let them go");

        public Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken)
            => held.CoveredUntilAsync(networkId, serviceId, cancellationToken);

        public Task<DateTime?> HeardWholeAtAsync(int networkId, int serviceId, CancellationToken cancellationToken)
            => held.HeardWholeAtAsync(networkId, serviceId, cancellationToken);

        public Task<IReadOnlyList<Programme>> ListAfterAsync(
            long revision,
            int rows,
            CancellationToken cancellationToken)
            => held.ListAfterAsync(revision, rows, cancellationToken);

        public Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
            => held.ForgetEverythingAsync(cancellationToken);

        public Task<IReadOnlyList<Programme>> ListGroupedAsync(CancellationToken cancellationToken)
            => held.ListGroupedAsync(cancellationToken);
    }
}
