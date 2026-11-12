using Carina.Domain.Base;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Tests.Notifications;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWatchRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Asked = new(2031, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Starts = Asked.AddHours(1);

    private readonly WatchedProgrammes watched = new(database);

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact(DisplayName = "BR-NS-003: the row held for a broadcast is found by the broadcast whatever state it is in, and none is found for a broadcast nobody asked for")]
    public async Task TheRowHeldForABroadcastIsFoundByTheBroadcast()
    {
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeId another = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId watch = await watched.AskedAsync(programme, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(watch);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IProgrammeWatchRepository rows = Rows(scope);

        ProgrammeWatch? found = await rows.FindOfAsync(programme, Cancel);
        ProgrammeWatch? none = await rows.FindOfAsync(another, Cancel);

        Assert.NotNull(found);
        Assert.Equal(watch, found.Id);
        Assert.Equal(ProgrammeWatchState.Cancelled, found.State);
        Assert.Null(none);
    }

    [Fact(DisplayName = "BR-NS-003: every row is listed, the ones that are settled among them, the earliest start first")]
    public async Task EveryRowIsListedTheEarliestStartFirst()
    {
        ProgrammeId later = await watched.AnnouncedAsync(Starts.AddHours(2), Asked);
        ProgrammeId sooner = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeId between = await watched.AnnouncedAsync(Starts.AddHours(1), Asked);
        ProgrammeWatchId third = await watched.AskedAsync(later, Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId first = await watched.AskedAsync(sooner, Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId second = await watched.AskedAsync(between, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(second);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IReadOnlyList<ProgrammeWatch> listed = await Rows(scope).ListAsync(Cancel);

        ProgrammeWatchId[] mine = [first, second, third];
        Assert.Equal(mine, listed.Select(row => row.Id).Where(mine.Contains));
        Assert.Equal(ProgrammeWatchState.Cancelled, listed.Single(row => row.Id.Equals(second)).State);
    }

    [Fact(DisplayName = "BR-NS-003: a row that was told of is followed until the broadcast ends and no longer from the moment it ends, and a row that waits is followed past it")]
    public async Task ARowThatWasToldOfIsFollowedUntilTheBroadcastEnds()
    {
        ProgrammeWatchId told = await watched.AskedAsync(
            await watched.AnnouncedAsync(Starts, Asked), Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId waiting = await watched.AskedAsync(
            await watched.AnnouncedAsync(Starts, Asked), Asked, 3, NotificationDestinationId.New());
        ProgrammeWatchId withdrawn = await watched.AskedAsync(
            await watched.AnnouncedAsync(Starts, Asked), Asked, 3, NotificationDestinationId.New());
        await ToldAsync(told);
        await WithdrawnAsync(withdrawn);
        DateTime ends = Starts + WatchedProgrammes.Length;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IProgrammeWatchRepository rows = Rows(scope);

        IReadOnlyList<ProgrammeWatch> aSecondBefore = await rows.ListFollowedAsync(ends.AddSeconds(-1), Cancel);
        IReadOnlyList<ProgrammeWatch> atTheEnd = await rows.ListFollowedAsync(ends, Cancel);

        Assert.Contains(aSecondBefore, row => row.Id.Equals(told));
        Assert.Contains(aSecondBefore, row => row.Id.Equals(waiting));
        Assert.DoesNotContain(aSecondBefore, row => row.Id.Equals(withdrawn));
        Assert.DoesNotContain(atTheEnd, row => row.Id.Equals(told));
        Assert.Contains(atTheEnd, row => row.Id.Equals(waiting));
    }

    [Fact(DisplayName = "BR-NS-003: a settled row is removed and the same broadcast asked for again in one write")]
    public async Task ASettledRowGivesWayToANewOneForTheSameBroadcast()
    {
        ProgrammeId programme = await watched.AnnouncedAsync(Starts, Asked);
        ProgrammeWatchId before = await watched.AskedAsync(programme, Asked, 3, NotificationDestinationId.New());
        await WithdrawnAsync(before);

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            IProgrammeWatchRepository rows = Rows(scope);
            ProgrammeWatch settled = (await rows.FindAsync(before, Cancel))!;
            ProgrammeWatch again = ProgrammeWatch.Ask(
                ProgrammeWatchId.New(),
                programme,
                new WatchedBroadcast(Starts, Starts + WatchedProgrammes.Length, WatchedProgrammes.ProgrammeName, null),
                5,
                [NotificationDestinationId.New()],
                Asked.AddMinutes(10));

            await scope.ServiceProvider.GetRequiredService<IAtomicWrite>().AllOrNothingAsync(
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

    [Fact(DisplayName = "BR-ND-014: the rows that no longer wait and whose broadcast ended before a moment are cleared away, and the rest are kept")]
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

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IProgrammeWatchRepository rows = Rows(scope);

        await rows.RemoveSettledBeforeAsync(edge, Cancel);

        Assert.Null(await rows.FindAsync(over, Cancel));
        Assert.NotNull(await rows.FindAsync(onTheEdge, Cancel));
        Assert.NotNull(await rows.FindAsync(waiting, Cancel));
    }

    private async Task WithdrawnAsync(ProgrammeWatchId watch)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IProgrammeWatchRepository rows = Rows(scope);
        ProgrammeWatch held = (await rows.FindAsync(watch, Cancel))!;

        held.Cancel(Asked.AddMinutes(5));
        await rows.SaveAsync(held, Cancel);
    }

    private async Task ToldAsync(ProgrammeWatchId watch)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IProgrammeWatchRepository rows = Rows(scope);
        ProgrammeWatch held = (await rows.FindAsync(watch, Cancel))!;

        held.Notify(Starts.AddMinutes(-3));
        await rows.SaveAsync(held, Cancel);
    }

    private async Task<ProgrammeWatch> HeldForAsync(ProgrammeId programme)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return (await Rows(scope).FindOfAsync(programme, Cancel))!;
    }

    private static IProgrammeWatchRepository Rows(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IProgrammeWatchRepository>();
}
