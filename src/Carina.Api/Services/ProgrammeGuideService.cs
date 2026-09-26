using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;

namespace Carina.Api.Services;

public sealed record GuidePage(
    IReadOnlyList<Programme> Programmes,
    IReadOnlyList<ArchivedProgramme> Archived,
    IReadOnlyList<BroadcastStream> Streams,
    string ETag);

public sealed class ProgrammeGuideService(
    ProgrammeSearchScope scope,
    IProgrammeRepository programmes,
    IArchivedProgrammeRepository archive,
    IProgrammeSearchRepository searches,
    TimeProvider clock)
{
    public async Task<ServiceResult<GuidePage>> ReadAsync(
        TuneSystem system,
        GuideWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        BroadcastStream[] carried = [.. await scope.ListedAsync(system, cancellationToken)];
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
        HashSet<(int NetworkId, int ServiceId, int EventId, DateTime StartsAt)> already = found
            .Select(programme => (
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.EventId.Value,
                programme.StartsAt))
            .ToHashSet();
        ArchivedProgramme[] archived =
        [
            .. kept.Where(programme => !already.Contains((
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.EventId.Value,
                programme.StartsAt))),
        ];

        return ServiceResult<GuidePage>.Success(new GuidePage(
            found,
            archived,
            carried,
            ETag(carried, window, found, archived)));
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

        return ServiceResult<PaginatedList<ProgrammeMatch>>.Success(await searches.SearchAsync(
            await scope.BoundAsync(search, cancellationToken),
            clock.GetUtcNow().UtcDateTime,
            cancellationToken));
    }

    private static string ETag(
        IReadOnlyList<BroadcastStream> carried,
        GuideWindow window,
        IReadOnlyList<Programme> found,
        IReadOnlyList<ArchivedProgramme> archived)
    {
        byte[] served = JsonSerializer.SerializeToUtf8Bytes(new Served(
            window.From,
            window.To,
            [
                .. carried.Select(stream => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stream.NetworkId.Value}.{stream.TransportStreamId.Value}.{stream.Tuning.System}.{stream.Tuning.PhysicalChannel}.{string.Join(",", stream.Services.Select(service => service.Value))}")),
            ],
            [
                .. found.Select(programme => new HeldFace(
                    programme.NetworkId.Value,
                    programme.ServiceId.Value,
                    programme.EventId.Value,
                    programme.Revision)),
            ],
            [
                .. archived.Select(programme => new KeptFace(
                    programme.NetworkId.Value,
                    programme.ServiceId.Value,
                    programme.EventId.Value,
                    programme.StartsAt,
                    programme.EndsAt,
                    programme.Name,
                    programme.Summary,
                    programme.HasSubtitles,
                    programme.Genres,
                    programme.Items)),
            ]));

        return $"\"{Convert.ToHexStringLower(SHA256.HashData(served).AsSpan(0, 16))}\"";
    }

    private sealed record Served(
        DateTime From,
        DateTime To,
        IReadOnlyList<string> Streams,
        IReadOnlyList<HeldFace> Held,
        IReadOnlyList<KeptFace> Kept);

    private sealed record HeldFace(int NetworkId, int ServiceId, int EventId, long Revision);

    private sealed record KeptFace(
        int NetworkId,
        int ServiceId,
        int EventId,
        DateTime StartsAt,
        DateTime EndsAt,
        string Name,
        string Summary,
        bool HasSubtitles,
        IReadOnlyList<ProgrammeGenre> Genres,
        IReadOnlyList<ProgrammeItem> Items);
}
