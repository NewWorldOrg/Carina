using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record GuidePage(
    IReadOnlyList<Programme> Programmes,
    IReadOnlyList<ArchivedProgramme> Archived,
    IReadOnlyList<BroadcastStream> Streams,
    string ETag);

public sealed class ProgrammeGuideService(
    IBroadcastStreamDirectory directory,
    IBroadcastServiceRepository catalogue,
    IProgrammeRepository programmes,
    IArchivedProgrammeRepository archive,
    IProgrammeSearchRepository searches,
    IStreamVisitRepository visits)
{
    public async Task<ServiceResult<GuidePage>> ReadAsync(
        TuneSystem system,
        GuideWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        BroadcastStream[] carried = [.. await ListedAsync(system, cancellationToken)];
        ProgrammeService[] wanted =
        [
            .. carried.SelectMany(stream =>
                stream.Services.Select(service => new ProgrammeService(stream.NetworkId.Value, service.Value))),
        ];
        IReadOnlyList<Programme> found = await programmes.ListForServicesAsync(
            wanted,
            window.From,
            window.To,
            cancellationToken);

        IReadOnlyList<ArchivedProgramme> kept = await archive.ListAsync(
            wanted,
            window.From,
            window.To,
            cancellationToken);
        var already = found
            .Select(programme => (
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.EventId.Value,
                programme.StartsAt))
            .ToHashSet();

        return ServiceResult<GuidePage>.Success(new GuidePage(
            found,
            [
                .. kept.Where(programme => !already.Contains((
                    programme.NetworkId.Value,
                    programme.ServiceId.Value,
                    programme.EventId.Value,
                    programme.StartsAt))),
            ],
            carried,
            await ETagAsync(carried, window, cancellationToken)));
    }

    public async Task<ServiceResult<Programme>> FindAsync(
        ProgrammeId id,
        CancellationToken cancellationToken)
        => await programmes.FindAsync(id, cancellationToken) is { } programme
            ? ServiceResult<Programme>.Success(programme)
            : ServiceResult<Programme>.Failure("No programme is held under that name.");

    public async Task<ServiceResult<PaginatedList<ProgrammeMatch>>> SearchAsync(
        ProgrammeSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        ProgrammeSearch asked = search.System is { } system
            ? search.Over(await CarriedOnAsync(system, cancellationToken))
            : search.Except(WithheldIn(await catalogue.ListAsync(cancellationToken)));

        return ServiceResult<PaginatedList<ProgrammeMatch>>.Success(
            await searches.SearchAsync(asked, cancellationToken));
    }

    private static IReadOnlyList<ProgrammeService> WithheldIn(IReadOnlyList<BroadcastService> known)
        =>
        [
            .. known
                .Where(service => !service.ListedInTheGuide)
                .Select(service => new ProgrammeService(service.NetworkId.Value, service.ServiceId.Value)),
        ];

    private async Task<IReadOnlyList<BroadcastStream>> ListedAsync(
        TuneSystem system,
        CancellationToken cancellationToken)
    {
        var withheld = WithheldIn(await catalogue.ListAsync(cancellationToken))
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

    private async Task<IReadOnlyList<ProgrammeService>> CarriedOnAsync(
        TuneSystem system,
        CancellationToken cancellationToken)
        =>
        [
            .. (await ListedAsync(system, cancellationToken))
                .SelectMany(stream => stream.Services.Select(service =>
                    new ProgrammeService(stream.NetworkId.Value, service.Value))),
        ];

    private static string Columns(IReadOnlyList<BroadcastStream> carried)
    {
        string spelt = string.Join(
            ";",
            carried.Select(stream => string.Create(
                CultureInfo.InvariantCulture,
                $"{stream.NetworkId.Value}.{stream.TransportStreamId.Value}.{stream.Tuning.System}.{stream.Tuning.PhysicalChannel}.{string.Join(",", stream.Services.Select(service => service.Value))}")));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(spelt)).AsSpan(0, 8));
    }

    private async Task<string> ETagAsync(
        IReadOnlyList<BroadcastStream> carried,
        GuideWindow window,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StreamVisit> ledger = await visits.ListAsync(cancellationToken);
        var wanted = carried
            .Select(stream => (stream.NetworkId.Value, stream.TransportStreamId.Value))
            .ToHashSet();
        StreamVisit[] mine =
        [
            .. ledger.Where(visit => wanted.Contains((visit.NetworkId.Value, visit.TransportStreamId.Value))),
        ];
        long stamp = mine.Length == 0
            ? 0
            : mine.Max(visit => visit.LastAttemptedAt.Ticks);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"\"{mine.Length:x}-{stamp:x}-{Columns(carried)}-{window.From.Ticks:x}-{window.To.Ticks:x}\"");
    }
}
