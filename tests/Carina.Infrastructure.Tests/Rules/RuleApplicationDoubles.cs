using System.Collections;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

namespace Carina.Infrastructure.Tests.Rules;

internal sealed class HeldRules : IRuleRepository
{
    public List<Rule> Rules { get; } = [];

    public List<Guid> Saved { get; } = [];

    public Task<Rule?> FindAsync(RuleId id, CancellationToken cancellationToken)
        => Task.FromResult(Rules.FirstOrDefault(rule => rule.Id.Equals(id)));

    public Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(RuleMatcher.InPrecedence(Rules));

    public Task<IReadOnlyList<Rule>> ListEnabledByPrecedenceAsync(CancellationToken cancellationToken)
        => Task.FromResult(RuleMatcher.InPrecedence(Rules.Where(rule => rule.Enabled)));

    public Task AddAsync(Rule rule, CancellationToken cancellationToken)
    {
        Rules.Add(rule);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Rule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Saved.Add(rule.Id.Value);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Rule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Rules.RemoveAll(held => held.Id.Equals(rule.Id));

        return Task.CompletedTask;
    }
}

internal sealed class CountedStreams(IReadOnlyList<BroadcastStream> streams) : IBroadcastStreamDirectory
{
    public List<BroadcastStream> Carried { get; } = [.. streams];

    public int Reads { get; private set; }

    public Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<BroadcastStream>>([.. Carried]);
    }

    public Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IntendedStream>>(
        [
            .. Carried.Select(stream => new IntendedStream(
                stream.NetworkId,
                stream.TransportStreamId,
                stream.Tuning,
                stream.Services,
                StreamReach.Reachable)),
        ]);
}

internal sealed class CountedServices : IBroadcastServiceRepository
{
    public List<BroadcastService> Services { get; } = [];

    public int Reads { get; private set; }

    public Task<BroadcastService?> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Services.FirstOrDefault(service =>
            service.NetworkId.Equals(networkId) && service.ServiceId.Equals(serviceId)));

    public Task<IReadOnlyList<BroadcastService>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<BroadcastService>>([.. Services]);
    }

    public Task AddAsync(BroadcastService service, CancellationToken cancellationToken)
    {
        Services.Add(service);

        return Task.CompletedTask;
    }

    public Task SaveAsync(BroadcastService service, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RemoveAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken)
        => Task.FromResult(
            Services.RemoveAll(service =>
                service.NetworkId.Equals(networkId) && service.ServiceId.Equals(serviceId)) > 0);
}

internal sealed class GuideThatBreaksOnTheFirstPass(IReadOnlyList<ProgrammeMatch> carried)
    : IReadOnlyList<ProgrammeMatch>
{
    private int passes;

    public int Passes => passes;

    public int Count => carried.Count;

    public ProgrammeMatch this[int index] => carried[index];

    public IEnumerator<ProgrammeMatch> GetEnumerator()
        => passes++ is 0
            ? throw new InvalidOperationException("The guide could not be read on this pass.")
            : carried.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
