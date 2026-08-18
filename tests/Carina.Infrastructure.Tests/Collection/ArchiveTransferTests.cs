using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

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

        await Add(context, network, 1, Now.AddDays(-400));

        Assert.Equal(0, (await Transfer(context).RunAsync(Cancel)).Forgotten);
    }

    [Fact]
    public async Task WithARetentionSetTheArchiveLetsGoOfWhatIsOlderThanIt()
    {
        int network = BroadcastIds.NextNetwork();
        await using CarinaDbContext context = database.Open();

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

    private static async Task Add(
        CarinaDbContext context,
        int network,
        int carried,
        DateTime startsAt,
        bool isShadow = false)
    {
        await new ProgrammeRepository(context).AddAsync(
            Programme.Discover(
                new ProgrammeBroadcast(
                    Id(network, carried),
                    new TransportStreamId(32_736),
                    startsAt,
                    startsAt.AddMinutes(30),
                    "ニュース",
                    string.Empty,
                    isShadow),
                startsAt),
            Cancel);
        await context.SaveChangesAsync(Cancel);
    }

    private static ProgrammeId Id(int network, int carried)
        => new(new NetworkId(network), new ServiceId(1049), new EventId(carried));

    private static ArchiveTransfer Transfer(CarinaDbContext context, CollectionSettings? settings = null)
        => new(
            new ProgrammeRepository(context),
            new ArchivedProgrammeRepository(context),
            settings ?? new CollectionSettings(),
            new FixedClock(Now),
            NullLogger<ArchiveTransfer>.Instance);
}

internal sealed class FixedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
}
