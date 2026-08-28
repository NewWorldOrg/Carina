using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Programmes;

public sealed record ProgrammeSearchBounds(
    IReadOnlyList<BroadcastStream> Streams,
    IReadOnlyList<ProgrammeService> Withheld)
{
    public IReadOnlyList<BroadcastStream> Listed(TuneSystem system)
    {
        var withheld = Withheld.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return
        [
            .. Streams
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

    public ProgrammeSearch Bound(ProgrammeSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);

        return search.System is { } system ? search.Over(CarriedOn(system)) : search.Except(Withheld);
    }

    private IReadOnlyList<ProgrammeService> CarriedOn(TuneSystem system)
        =>
        [
            .. Listed(system)
                .SelectMany(stream => stream.Services.Select(service =>
                    new ProgrammeService(stream.NetworkId.Value, service.Value))),
        ];
}

public sealed class ProgrammeSearchScope(
    IBroadcastStreamDirectory directory,
    IBroadcastServiceRepository catalogue)
{
    public async Task<ProgrammeSearchBounds> ReadAsync(CancellationToken cancellationToken)
        => new(
            await directory.ListAsync(cancellationToken),
            await WithheldAsync(cancellationToken));

    public async Task<ProgrammeSearch> BoundAsync(ProgrammeSearch search, CancellationToken cancellationToken)
        => (await ReadAsync(cancellationToken)).Bound(search);

    public async Task<IReadOnlyList<BroadcastStream>> ListedAsync(
        TuneSystem system,
        CancellationToken cancellationToken)
        => (await ReadAsync(cancellationToken)).Listed(system);

    public async Task<IReadOnlyList<ProgrammeService>> WithheldAsync(CancellationToken cancellationToken)
        =>
        [
            .. (await catalogue.ListAsync(cancellationToken))
                .Where(service => !service.ListedInTheGuide)
                .Select(service => new ProgrammeService(service.NetworkId.Value, service.ServiceId.Value)),
        ];
}
