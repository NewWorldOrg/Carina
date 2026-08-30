using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Rules;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RuleRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARuleComesBackWithEverythingItWasStoredUnder()
    {
        Rule written = Written(1, "the loud one", priority: 40, marginBefore: 30, marginAfter: 60);
        await ClearAsync();
        await AddAsync(written);

        await using CarinaDbContext context = database.Open();
        Rule? found = await new RuleRepository(context).FindAsync(written.Id, Cancel);

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

        await using CarinaDbContext context = database.Open();
        IReadOnlyList<Rule> found = await new RuleRepository(context).ListAsync(Cancel);

        Assert.Equal(["off", "on"], [.. found.Select(rule => rule.Name)]);
    }

    [Fact]
    public async Task ARuleThatWasRewrittenComesBackRewritten()
    {
        Rule written = Written(1, "before", priority: 30);
        await ClearAsync();
        await AddAsync(written);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new RuleRepository(writing);
            Rule held = (await repository.FindAsync(written.Id, Cancel))!;
            held.Rewrite("after", new RuleQuery("keyword=river"), new Priority(50), Margin.None, Margin.None);
            held.Disable();
            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        Rule? found = await new RuleRepository(reading).FindAsync(written.Id, Cancel);

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

        await using (CarinaDbContext removing = database.Open())
        {
            var repository = new RuleRepository(removing);
            await repository.RemoveAsync((await repository.FindAsync(written.Id, Cancel))!, Cancel);
        }

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new RuleRepository(reading).FindAsync(written.Id, Cancel));
    }

    [Fact]
    public async Task NothingComesBackWhenThereIsNoRule()
    {
        await ClearAsync();

        Assert.Empty(await ReadAsync());
    }

    private async Task<string[]> ReadAsync()
    {
        await using CarinaDbContext context = database.Open();

        return
        [
            .. (await new RuleRepository(context).ListEnabledByPrecedenceAsync(Cancel))
                .Select(rule => rule.Name),
        ];
    }

    private async Task AddAsync(Rule rule)
    {
        await using CarinaDbContext context = database.Open();
        await new RuleRepository(context).AddAsync(rule, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();
        await context.Set<Reservation>().ExecuteDeleteAsync(Cancel);
        await context.Set<Rule>().ExecuteDeleteAsync(Cancel);
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
}
