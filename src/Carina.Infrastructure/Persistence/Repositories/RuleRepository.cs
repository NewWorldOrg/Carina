using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class RuleRepository(CarinaDbContext context) : IRuleRepository
{
    public async Task<Rule?> FindAsync(RuleId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Rule>().FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken)
        => RuleMatcher.InPrecedence(await context.Set<Rule>().ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<Rule>> ListEnabledByPrecedenceAsync(CancellationToken cancellationToken)
        => RuleMatcher.InPrecedence(
            await context.Set<Rule>().Where(rule => rule.Enabled).ToListAsync(cancellationToken));

    public async Task AddAsync(Rule rule, CancellationToken cancellationToken)
    {
        context.Add(rule);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(Rule rule, CancellationToken cancellationToken)
    {
        context.Update(rule);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Rule rule, CancellationToken cancellationToken)
    {
        context.Remove(rule);

        await context.SaveChangesAsync(cancellationToken);
    }
}
