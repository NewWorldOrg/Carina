namespace Carina.Domain.Rules;

public interface IRuleRepository
{
    Task<Rule?> FindAsync(RuleId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Rule>> ListEnabledByPrecedenceAsync(CancellationToken cancellationToken);

    Task AddAsync(Rule rule, CancellationToken cancellationToken);

    Task SaveAsync(Rule rule, CancellationToken cancellationToken);

    Task RemoveAsync(Rule rule, CancellationToken cancellationToken);
}
