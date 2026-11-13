using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Rules;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RuleRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ahead = Now.AddDays(24);

    private const int Network = 4;

    private const int Listed = 3149;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task ARuleComesBackWithEverythingItWasStoredUnder()
    {
        Rule written = Written(1, "the loud one", priority: 40, marginBefore: 30, marginAfter: 60);
        await ClearAsync();
        await AddAsync(written);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Rule? found = await Rules(scope).FindAsync(written.Id, Cancel);

        Assert.NotNull(found);
        Assert.Equal("the loud one", found.Name);
        Assert.Equal("keyword=hill", found.Query.Value);
        Assert.Equal(40, found.Priority.Value);
        Assert.True(found.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), found.MarginBefore.Value);
        Assert.Equal(TimeSpan.FromSeconds(60), found.MarginAfter.Value);
        Assert.Equal(written.CreatedAt, found.CreatedAt);
    }

    [Fact]
    public async Task TheLouderRuleComesBackFirstThoughItSortsLastByIdentifierAndWasWrittenLast()
    {
        await ClearAsync();
        await AddAsync(Written(1, "quiet", priority: 10, at: Now.AddDays(-9)));
        await AddAsync(Written(9, "loud", priority: 40, at: Now.AddDays(-1)));

        Assert.Equal(["loud", "quiet"], await ReadAsync());
    }

    [Fact]
    public async Task RulesOfEqualWeightComeBackWithTheOneWrittenEarlierFirst()
    {
        await ClearAsync();
        await AddAsync(Written(9, "earlier", priority: 30, at: Now.AddDays(-9)));
        await AddAsync(Written(1, "later", priority: 30, at: Now.AddDays(-2)));

        Assert.Equal(["earlier", "later"], await ReadAsync());
    }

    [Fact]
    public async Task RulesWrittenAtTheSameMomentComeBackWithTheSmallerIdentifierFirst()
    {
        await ClearAsync();
        await AddAsync(Written(9, "larger", priority: 30, at: Now.AddDays(-9)));
        await AddAsync(Written(1, "smaller", priority: 30, at: Now.AddDays(-9)));

        Assert.Equal(["smaller", "larger"], await ReadAsync());
    }

    [Fact]
    public async Task ARuleThatIsOffIsLeftOutOfWhatComesBackWhileTheOneThatIsOnComesBack()
    {
        await ClearAsync();
        await AddAsync(Written(1, "on", priority: 30));
        await AddAsync(Written(2, "off", priority: 40, enabled: false));

        Assert.Equal(["on"], await ReadAsync());
    }

    [Fact]
    public async Task EverythingComesBackFromTheListThatDoesNotAskWhetherARuleIsOn()
    {
        await ClearAsync();
        await AddAsync(Written(1, "on", priority: 30));
        await AddAsync(Written(2, "off", priority: 40, enabled: false));

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReadOnlyList<Rule> found = await Rules(scope).ListAsync(Cancel);

        Assert.Equal(["off", "on"], [.. found.Select(rule => rule.Name)]);
    }

    [Fact]
    public async Task ARuleThatWasRewrittenComesBackRewritten()
    {
        Rule written = Written(1, "before", priority: 30);
        await ClearAsync();
        await AddAsync(written);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IRuleRepository repository = Rules(writing);
            Rule held = (await repository.FindAsync(written.Id, Cancel))!;
            held.Rewrite("after", new RuleQuery("keyword=river"), new Priority(50), Margin.None, Margin.None, true);
            held.Disable();
            await repository.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Rule? found = await Rules(reading).FindAsync(written.Id, Cancel);

        Assert.NotNull(found);
        Assert.Equal("after", found.Name);
        Assert.Equal("keyword=river", found.Query.Value);
        Assert.Equal(50, found.Priority.Value);
        Assert.False(found.Enabled);
    }

    [Fact]
    public async Task ARuleThatWasRemovedIsGone()
    {
        Rule written = Written(1, "going", priority: 30);
        await ClearAsync();
        await AddAsync(written);

        await using (AsyncServiceScope removing = container.CreateAsyncScope())
        {
            IRuleRepository repository = Rules(removing);
            await repository.RemoveAsync((await repository.FindAsync(written.Id, Cancel))!, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await Rules(reading).FindAsync(written.Id, Cancel));
    }

    [Fact]
    public async Task NothingComesBackWhenThereIsNoRule()
    {
        await ClearAsync();

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task ARuleTakenOutFromUnderAReservationIsRefusedByTheDatabaseItself()
    {
        Rule staying = Numbered(0x75);
        await AddAsync(staying);
        await ClaimedAsync(await StandingAsync(9231, staying.Id));

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Context(scope).Database.ExecuteSqlRawAsync(
                "DELETE FROM rule WHERE id = {0}",
                [staying.Id.Value],
                Cancel));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refusal.SqlState);
        Assert.Equal("fk_reservation_rule_rule_id", refusal.ConstraintName);
        Assert.NotNull(await ReadRuleAsync(staying.Id));
    }

    private async Task<string[]> ReadAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return
        [
            .. (await Rules(scope).ListEnabledByPrecedenceAsync(Cancel))
                .Select(rule => rule.Name),
        ];
    }

    private async Task AddAsync(Rule rule)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Rules(scope).AddAsync(rule, Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Context(scope).Set<Reservation>().ExecuteDeleteAsync(Cancel);
        await Context(scope).Set<Rule>().ExecuteDeleteAsync(Cancel);
    }

    private static Rule Written(
        int identifier,
        string name,
        int priority,
        bool enabled = true,
        int marginBefore = 0,
        int marginAfter = 0,
        DateTime? at = null)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            name,
            new RuleQuery("keyword=hill"),
            new Priority(priority),
            enabled,
            Margin.OfSeconds(marginBefore),
            Margin.OfSeconds(marginAfter),
            at ?? Now.AddDays(-30));

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IRuleRepository Rules(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRuleRepository>();

    private async Task<Reservation> ClaimedAsync(Reservation reservation)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.True(await Contract(scope).ClaimAsync(
            reservation.Id,
            Now.AddMinutes(-3),
            Cancel));

        return reservation;
    }

    private async Task<Reservation> StandingAsync(
        int carried,
        RuleId ruleId,
        ReservationState state = ReservationState.Scheduled)
    {
        DateTime opens = Ahead.AddHours(carried % 32);
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(
                new NetworkId(Network),
                new ServiceId(Listed),
                new EventId(carried),
                opens),
            ruleId,
            Priority.Default,
            opens,
            opens.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                $"a broadcast {carried}",
                "what it is about",
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            state,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now,
            state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await Reservations(scope).AddAsync(reservation, Cancel);

        return reservation;
    }

    private async Task<Rule?> ReadRuleAsync(RuleId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Rules(scope).FindAsync(id, Cancel);
    }

    private static Rule Numbered(int identifier)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            $"rule {identifier:x}",
            new RuleQuery("keyword=upland"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static IReservationRecordingContract Contract(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRecordingContract>();

    private static IReservationRepository Reservations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRepository>();
}
