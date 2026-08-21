using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

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
        await Add(context, network, 1, Now.AddDays(-3));

        Transferred moved = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(1, moved.Kept);
        Assert.Equal(1, moved.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));
        Assert.Single(await new ArchivedProgrammeRepository(reading).ListAsync(
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
        await Add(context, network, 1, Now.AddHours(-2));

        Transferred moved = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(0, moved.Kept);

        await using CarinaDbContext reading = database.Open();

        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));
    }

    [Fact]
    public async Task AShadowIsLetGoOfWithoutBeingKept()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(context, network, 1, Now.AddDays(-3), isShadow: true);

        Transferred moved = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(0, moved.Kept);
        Assert.Equal(1, moved.Discarded);
    }

    [Fact]
    public async Task WithNoRetentionSetTheArchiveKeepsEverything()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(context, network, 1, Now.AddDays(-400));

        Assert.Equal(0, (await Transfer(context).RunAsync(Cancel)).Forgotten);
    }

    [Fact]
    public async Task WithARetentionSetTheArchiveLetsGoOfWhatIsOlderThanIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(context, network, 1, Now.AddDays(-400));

        Transferred moved = await Transfer(
            context,
            new CollectionSettings { ArchiveRetention = TimeSpan.FromDays(365) }).RunAsync(Cancel);

        Assert.Equal(1, moved.Kept);

        await using CarinaDbContext reading = database.Open();

        Assert.Empty(await new ArchivedProgrammeRepository(reading).ListAsync(
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
        await AddMany(context, network, ArchiveTransfer.MostPerRun);

        Transferred moved = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(ArchiveTransfer.MostPerRun, moved.Kept);
        Assert.Equal(ArchiveTransfer.MostPerRun, moved.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ArchiveTransfer.MostPerRun, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
    }

    [Fact]
    public async Task PastTheCapWhatTheArchiveDidNotTakeStaysInTheGuide()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await AddMany(context, network, ArchiveTransfer.MostPerRun + 1);

        Transferred first = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(ArchiveTransfer.MostPerRun, first.Kept);
        Assert.Equal(ArchiveTransfer.MostPerRun, first.Discarded);

        await using CarinaDbContext again = database.Open();

        Assert.Equal(1, await again.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ArchiveTransfer.MostPerRun, await again.Set<ArchivedProgramme>().CountAsync(Cancel));

        Transferred second = await Transfer(again).RunAsync(Cancel);

        Assert.Equal(1, second.Kept);
        Assert.Equal(1, second.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<Programme>().CountAsync(Cancel));
        Assert.Equal(ArchiveTransfer.MostPerRun + 1, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
    }

    [Fact]
    public async Task AProgrammeWhoseEndIsUndecidedIsNotLetGoOf()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(context, network, 1, Now.AddDays(-3), endIsUndecided: true);

        Transferred moved = await Transfer(context).RunAsync(Cancel);

        Assert.Equal(0, moved.Kept);
        Assert.Equal(0, moved.Discarded);

        await using CarinaDbContext reading = database.Open();

        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));
    }

    [Fact]
    public async Task WhenTheGuideCannotLetGoTheArchiveKeepsNothingEither()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

        await Empty(context);
        await Add(context, network, 1, Now.AddDays(-3));

        var transfer = new ArchiveTransfer(
            new StubbornProgrammes(new ProgrammeRepository(context)),
            new ArchivedProgrammeRepository(context),
            new DatabaseAtomicWrite(context),
            new CollectionSettings(),
            new FixedClock(Now),
            NullLogger<ArchiveTransfer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transfer.RunAsync(Cancel));

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(0, await reading.Set<ArchivedProgramme>().CountAsync(Cancel));
        Assert.NotNull(await new ProgrammeRepository(reading).FindAsync(Id(network, 1), Cancel));

        await Empty(reading);
    }

    private static async Task Add(
        CarinaDbContext context,
        int network,
        int carried,
        DateTime startsAt,
        bool isShadow = false,
        bool endIsUndecided = false)
    {
        await new ProgrammeRepository(context).AddAsync(
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
        await context.SaveChangesAsync(Cancel);
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

    private static ArchiveTransfer Transfer(CarinaDbContext context, CollectionSettings? settings = null)
        => new(
            new ProgrammeRepository(context),
            new ArchivedProgrammeRepository(context),
            new DatabaseAtomicWrite(context),
            settings ?? new CollectionSettings(),
            new FixedClock(Now),
            NullLogger<ArchiveTransfer>.Instance);
}

internal sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
}

internal sealed class StubbornProgrammes(IProgrammeRepository held) : IProgrammeRepository
{
    public Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
        => held.FindAsync(id, cancellationToken);

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

    public Task SaveAsync(Programme programme, CancellationToken cancellationToken)
        => held.SaveAsync(programme, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => held.ListEndedBeforeAsync(at, rows, cancellationToken);

    public Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken)
        => throw new InvalidOperationException("the guide would not let them go");

    public Task<DateTime?> CoveredUntilAsync(int networkId, int serviceId, CancellationToken cancellationToken)
        => held.CoveredUntilAsync(networkId, serviceId, cancellationToken);

    public Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken)
        => held.ListAfterAsync(revision, rows, cancellationToken);

    public Task<long> NextRevisionAsync(CancellationToken cancellationToken)
        => held.NextRevisionAsync(cancellationToken);

    public Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
        => held.ForgetEverythingAsync(cancellationToken);
}
