using System.Globalization;

using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record GuidePage(
    IReadOnlyList<Programme> Programmes,
    IReadOnlyList<BroadcastStream> Streams,
    string ETag);

public sealed class ProgrammeGuideService(
    IBroadcastStreamDirectory directory,
    IProgrammeRepository programmes,
    IStreamVisitRepository visits)
{
    public async Task<ServiceResult<GuidePage>> ReadAsync(
        TuneSystem system,
        GuideWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        BroadcastStream[] carried =
        [
            .. (await directory.ListAsync(cancellationToken))
                .Where(stream => stream.Tuning.System == system),
        ];
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

        return ServiceResult<GuidePage>.Success(new GuidePage(
            found,
            carried,
            await ETagAsync(carried, window, cancellationToken)));
    }

    public async Task<ServiceResult<Programme>> FindAsync(
        ProgrammeId id,
        CancellationToken cancellationToken)
        => await programmes.FindAsync(id, cancellationToken) is { } programme
            ? ServiceResult<Programme>.Success(programme)
            : ServiceResult<Programme>.Failure("No programme is held under that name.");

    public async Task<ServiceResult<PaginatedList<Programme>>> SearchAsync(
        ProgrammeSearch search,
        CancellationToken cancellationToken)
        => ServiceResult<PaginatedList<Programme>>.Success(
            await programmes.SearchAsync(search, cancellationToken));

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
            $"\"{mine.Length:x}-{stamp:x}-{window.From.Ticks:x}-{window.To.Ticks:x}\"");
    }
}
