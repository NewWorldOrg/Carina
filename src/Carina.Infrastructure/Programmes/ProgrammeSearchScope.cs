using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Programmes;

public sealed class ProgrammeSearchScope(
    IBroadcastStreamDirectory directory,
    IBroadcastServiceRepository catalogue)
{
    public async Task<ProgrammeSearch> BoundAsync(ProgrammeSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        return search.System is { } system
            ? search.Over(await CarriedOnAsync(system, cancellationToken))
            : search.Except(await WithheldAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<BroadcastStream>> ListedAsync(
        TuneSystem system,
        CancellationToken cancellationToken)
    {
        var withheld = (await WithheldAsync(cancellationToken))
            .Select(service => (service.NetworkId, service.ServiceId))
            .ToHashSet();

        return
        [
            .. (await directory.ListAsync(cancellationToken))
                .Where(stream => stream.Tuning.System == system)
                .Select(stream => stream with
                {
                    Services =
                    [
                        .. stream.Services.Where(service =>
                            !withheld.Contains((stream.NetworkId.Value, service.Value))),
                    ],
                }),
        ];
    }

    public async Task<IReadOnlyList<ProgrammeService>> WithheldAsync(CancellationToken cancellationToken)
        =>
        [
            .. (await catalogue.ListAsync(cancellationToken))
                .Where(service => !service.ListedInTheGuide)
                .Select(service => new ProgrammeService(service.NetworkId.Value, service.ServiceId.Value)),
        ];

    private async Task<IReadOnlyList<ProgrammeService>> CarriedOnAsync(
        TuneSystem system,
        CancellationToken cancellationToken)
        =>
        [
            .. (await ListedAsync(system, cancellationToken))
                .SelectMany(stream => stream.Services.Select(service =>
                    new ProgrammeService(stream.NetworkId.Value, service.Value))),
        ];
}
