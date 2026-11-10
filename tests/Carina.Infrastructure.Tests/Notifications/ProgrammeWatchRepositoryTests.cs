using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Infrastructure.Tests.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWatchRepositoryTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Asked = new(2031, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Starts = Asked.AddHours(1);

    private readonly WatchedProgrammes watched = new(database);

    [Fact(DisplayName = "BR-NS-003: the row held for a broadcast is found by the broadcast whatever state it is in, and none is found for a broadcast nobody asked for")]
    public async Task TheRowHeldForABroadcastIsFoundByTheBroadcast()
    {
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeId another = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(watch);

        await using CarinaDbContext context = database.Open();
        ProgrammeWatchRepository rows = new(context);

        ProgrammeWatch? found = await rows.FindOfAsync(programme, Cancel);
        ProgrammeWatch? none = await rows.FindOfAsync(another, Cancel);

        Assert.NotNull(found);
        Assert.Equal(watch, found.Id);
        Assert.Equal(ProgrammeWatchState.Cancelled, found.State);
        Assert.Null(none);
    }

    [Fact(DisplayName = "D-5: every row is listed, the ones that are settled among them, the earliest start first")]
    public async Task EveryRowIsListedTheEarliestStartFirst()
    {
        ProgrammeId later = await watched.AnnouncedAsync(Starts.AddHours(2), Asked);
        ProgrammeId sooner = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeId between = await watched.AnnouncedAsync(Starts.AddHours(1), Asked);
        ProgrammeWatchId third = await watched.AskedAsync(later, Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId first = await watched.AskedAsync(sooner, Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId second = await watched.AskedAsync(between, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(second);

        await using CarinaDbContext context = database.Open();

        IReadOnlyList<ProgrammeWatch> listed = await new ProgrammeWatchRepository(context).ListAsync(Cancel);

        ProgrammeWatchId[] mine = [first, second, third];
        Assert.Equal(mine, listed.Select(row => row.Id).Where(mine.Contains));
        Assert.Equal(ProgrammeWatchState.Cancelled, listed.Single(row => row.Id.Equals(second)).State);
    }

    [Fact(DisplayName = "BR-NS-003: a settled row is removed and the same broadcast asked for again in one write")]
    public async Task ASettledRowGivesWayToANewOneForTheSameBroadcast()
    {
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId before = await watched.AskedAsync(programme, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(before);

        await using (CarinaDbContext context = database.Open())
        {
            ProgrammeWatchRepository rows = new(context);
            ProgrammeWatch settled = (await rows.FindAsync(before, Cancel))!;
            ProgrammeWatch again = ProgrammeWatch.Ask(
                ProgrammeWatchId.New(),
                programme,
                new WatchedBroadcast(Starts, Starts + WatchedProgrammes.Length, WatchedProgrammes.ProgrammeName, null),
                5,
                [NotificationDestinationId.New()],
                Asked.AddMinutes(10));

            await new DatabaseAtomicWrite(context).AllOrNothingAsync(
                async inner =>
                {
                    await rows.RemoveAsync(settled, inner);
                    await rows.AddAsync(again, inner);

                    return true;
                },
                Cancel);
        }

        ProgrammeWatch held = await HeldForAsync(programme);
        Assert.NotEqual(before, held.Id);
        Assert.Equal(ProgrammeWatchState.Waiting, held.State);
        Assert.Equal(5, held.LeadMinutes);
        Assert.Null(held.StationName);
    }

    [Fact(DisplayName = "D-5: the rows that no longer wait and whose broadcast ended before a moment are cleared away, and the rest are kept")]
    public async Task TheRowsThatNoLongerWaitAreClearedAwayOnceTheirBroadcastIsLongOver()
    {
        DateTime asked = Asked.AddYears(-1);
        DateTime starts = asked.AddHours(1);
        DateTime edge = starts.AddMinutes(10) + WatchedProgrammes.Length;
        ProgrammeWatchId over = await watched.AskedAsync(
            await watched.AnnouncedAsync(starts, asked), asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId onTheEdge = await watched.AskedAsync(
            await watched.AnnouncedAsync(starts.AddMinutes(10), asked), asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId waiting = await watched.AskedAsync(
            await watched.AnnouncedAsync(starts, asked), asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(over);
        await WithdrawnAsync(onTheEdge);

        await using CarinaDbContext context = database.Open();
        ProgrammeWatchRepository rows = new(context);

        int cleared = await rows.RemoveSettledBeforeAsync(edge, Cancel);

        Assert.Equal(1, cleared);
        Assert.Null(await rows.FindAsync(over, Cancel));
        Assert.NotNull(await rows.FindAsync(onTheEdge, Cancel));
        Assert.NotNull(await rows.FindAsync(waiting, Cancel));
    }

    private async Task WithdrawnAsync(ProgrammeWatchId watch)
    {
        await using CarinaDbContext context = database.Open();
        ProgrammeWatchRepository rows = new(context);
        ProgrammeWatch held = (await rows.FindAsync(watch, Cancel))!;

        held.Cancel(Asked.AddMinutes(5));
        await rows.SaveAsync(held, Cancel);
    }

    private async Task<ProgrammeWatch> HeldForAsync(ProgrammeId programme)
    {
        await using CarinaDbContext context = database.Open();

        return (await new ProgrammeWatchRepository(context).FindOfAsync(programme, Cancel))!;
    }
}
