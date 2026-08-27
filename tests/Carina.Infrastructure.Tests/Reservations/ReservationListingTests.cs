using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationListingTests(RepositoryDatabase database)
{
    private const int Network = 32736;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ThePageSaysHowManyThereAreAndHandsBackOnlyTheOnesOnIt()
    {
        const int Service = 1101;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(4)),
            Planned(Service, Now.AddHours(6)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service) with { },
            perPage: 2);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.LastPage);
        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(2, page.PerPage);
    }

    [Fact]
    public async Task TheSecondPageCarriesOnWhereTheFirstStopped()
    {
        const int Service = 1102;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(4)),
            Planned(Service, Now.AddHours(6)));

        PaginatedList<Reservation> first = await ListAsync(Only(Service), perPage: 2);
        PaginatedList<Reservation> second = await ListAsync(Only(Service), perPage: 2, page: 2);

        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.Equal(Now.AddHours(6), second.Items[0].StartAt);
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        const int Wanted = 1103;
        const int Beside = 1104;
        await AddAsync(Planned(Wanted, Now.AddHours(2)), Planned(Beside, Now.AddHours(2)));

        PaginatedList<Reservation> page = await ListAsync(Only(Wanted));

        Assert.Single(page.Items);
        Assert.Equal(Wanted, page.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task MoreThanOneChannelIsAskedForAtOnce()
    {
        const int First = 1105;
        const int Second = 1106;
        const int Beside = 1107;
        await AddAsync(
            Planned(First, Now.AddHours(2)),
            Planned(Second, Now.AddHours(2)),
            Planned(Beside, Now.AddHours(2)));

        PaginatedList<Reservation> page = await ListAsync(new ReservationConditions
        {
            Channels = [new ProgrammeService(Network, First), new ProgrammeService(Network, Second)],
        });

        Assert.Equal(2, page.Total);
        Assert.Equal([First, Second], page.Items.Select(item => item.ServiceId.Value).Order());
    }

    [Fact]
    public async Task AReservationARuleMadeAndOneAHandMadeAreToldApart()
    {
        const int Service = 1108;
        RuleId rule = await DraftedAsync("Drama");
        await AddAsync(Planned(Service, Now.AddHours(2)), Planned(Service, Now.AddHours(4), rule: rule));

        PaginatedList<Reservation> byHand = await ListAsync(
            Only(Service) with { Origin = ReservationOrigin.ByHand });
        PaginatedList<Reservation> byRule = await ListAsync(
            Only(Service) with { Origin = ReservationOrigin.ByRule });

        Assert.Single(byHand.Items);
        Assert.False(byHand.Items[0].IsRuleBorn);
        Assert.Single(byRule.Items);
        Assert.True(byRule.Items[0].IsRuleBorn);
    }

    [Fact]
    public async Task AKeywordReadsWhatTheReservationKeptOfTheProgramme()
    {
        const int Service = 1109;
        await AddAsync(
            Planned(Service, Now.AddHours(2), snapshot: ReservationFixtures.Snapshot("Harbour report", "The tide")),
            Planned(Service, Now.AddHours(4), snapshot: ReservationFixtures.Snapshot("Kitchen notes", "Vegetables")));

        PaginatedList<Reservation> byName = await ListAsync(Only(Service) with { Keyword = "harbour" });
        PaginatedList<Reservation> bySummary = await ListAsync(Only(Service) with { Keyword = "vegetables" });
        PaginatedList<Reservation> byNeither = await ListAsync(Only(Service) with { Keyword = "orchestra" });

        Assert.Equal("Harbour report", Assert.Single(byName.Items).SnapshotName);
        Assert.Equal("Kitchen notes", Assert.Single(bySummary.Items).SnapshotName);
        Assert.Empty(byNeither.Items);
    }

    [Fact]
    public async Task AKeywordDoesNotCareWhichCaseItWasTypedIn()
    {
        const int Service = 1110;
        await AddAsync(
            Planned(Service, Now.AddHours(2), snapshot: ReservationFixtures.Snapshot("Harbour report", "The tide")));

        Assert.Single((await ListAsync(Only(Service) with { Keyword = "HARBOUR" })).Items);
        Assert.Single((await ListAsync(Only(Service) with { Keyword = "harbour" })).Items);
    }

    [Fact]
    public async Task ASpanNarrowsTheListToWhatStartsInsideIt()
    {
        const int Service = 1111;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(8)),
            Planned(Service, Now.AddHours(20)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service),
            from: Now.AddHours(4),
            to: Now.AddHours(12));

        Assert.Equal(Now.AddHours(8), Assert.Single(page.Items).StartAt);
    }

    [Fact]
    public async Task TheStartOfASpanIsInsideItAndTheEndIsNot()
    {
        const int Service = 1112;
        await AddAsync(Planned(Service, Now.AddHours(4)), Planned(Service, Now.AddHours(12)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service),
            from: Now.AddHours(4),
            to: Now.AddHours(12));

        Assert.Equal(Now.AddHours(4), Assert.Single(page.Items).StartAt);
    }

    [Fact]
    public async Task TheListIsSortedTheWayItWasAskedFor()
    {
        const int Service = 1113;
        await AddAsync(
            Planned(Service, Now.AddHours(2), priority: new Priority(10)),
            Planned(Service, Now.AddHours(4), priority: new Priority(50)),
            Planned(Service, Now.AddHours(6), priority: new Priority(30)));

        PaginatedList<Reservation> byStart = await ListAsync(Only(Service));
        PaginatedList<Reservation> byStartBackwards = await ListAsync(Only(Service), descending: true);
        PaginatedList<Reservation> byPriority = await ListAsync(Only(Service), sort: ReservationSort.Priority);
        PaginatedList<Reservation> byPriorityBackwards = await ListAsync(
            Only(Service),
            sort: ReservationSort.Priority,
            descending: true);

        Assert.Equal(Now.AddHours(2), byStart.Items[0].StartAt);
        Assert.Equal(Now.AddHours(6), byStartBackwards.Items[0].StartAt);
        Assert.Equal([10, 30, 50], byPriority.Items.Select(item => item.Priority.Value));
        Assert.Equal([50, 30, 10], byPriorityBackwards.Items.Select(item => item.Priority.Value));
    }

    [Fact]
    public async Task EveryReservationOnTheChannelComesBackWhenNothingNarrowsTheList()
    {
        const int Service = 1114;
        Reservation standing = Planned(Service, Now.AddHours(2));
        Reservation cancelled = Planned(Service, Now.AddHours(4));
        await AddAsync(standing, cancelled);
        await CancelAsync(cancelled.Id);

        PaginatedList<Reservation> page = await ListAsync(Only(Service));

        Assert.Equal([standing.Id, cancelled.Id], page.Items.Select(item => item.Id));
    }

    private static ReservationConditions Only(int serviceId)
        => new() { Channels = [new ProgrammeService(Network, serviceId)] };

    private static Reservation Planned(
        int serviceId,
        DateTime startsAt,
        Priority? priority = null,
        RuleId? rule = null,
        ProgrammeSnapshot? snapshot = null)
        => ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId, startsAt),
            priority: priority,
            ruleId: rule,
            startAt: startsAt,
            endAt: startsAt.AddHours(1),
            snapshot: snapshot);

    private async Task<PaginatedList<Reservation>> ListAsync(
        ReservationConditions conditions,
        DateTime? from = null,
        DateTime? to = null,
        ReservationSort sort = ReservationSort.StartAt,
        bool descending = false,
        int? page = null,
        int? perPage = null)
    {
        ReservationQuery query = ReservationQuery.For(from, to, sort, descending, page, perPage, conditions)!;

        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).ListAsync(query, Cancel);
    }

    private async Task<RuleId> DraftedAsync(string name)
    {
        RuleId id = RuleId.New();

        await using CarinaDbContext context = database.Open();
        context.Add(Rule.Draft(
            id,
            name,
            new RuleQuery("keyword=drama"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now));

        await context.SaveChangesAsync(Cancel);

        return id;
    }

    private async Task AddAsync(params Reservation[] reservations)
    {
        await using CarinaDbContext context = database.Open();
        var repository = new ReservationRepository(context);

        foreach (Reservation reservation in reservations)
        {
            await repository.AddAsync(reservation, Cancel);
        }
    }

    private async Task CancelAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();
        var repository = new ReservationRepository(context);
        Reservation found = (await repository.FindAsync(id, Cancel))!;

        found.Cancel();

        await repository.SaveAsync(found, Cancel);
    }
}
