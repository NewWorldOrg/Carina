using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Conventions.Tests.Fixtures;

public static class RuleRetirementFixtures
{
    public static Task RemovesARuleWithoutWithdrawingWhatItMade(IRuleRepository rules, Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.RemoveAsync(rule, CancellationToken.None);
    }

    public static Task<Rule?> ReadsARuleWithoutRemovingIt(IRuleRepository rules, RuleId id)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules.FindAsync(id, CancellationToken.None);
    }

    public static void TakesARuleOutOfTheLedgerBehindTheRepository(CarinaDbContext context, Rule rule)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Remove(rule);
    }

    public static void TakesRulesOutOfTheLedgerInABatchBehindTheRepository(
        CarinaDbContext context,
        IReadOnlyList<Rule> carried)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.RemoveRange(carried);
    }

    public static Task<int> TakesARuleOutOfTheLedgerWithoutTrackingIt(CarinaDbContext context, RuleId id)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Set<Rule>().Where(rule => rule.Id == id).ExecuteDeleteAsync(CancellationToken.None);
    }
}
