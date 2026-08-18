using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record ArchiveForgotten(int Forgotten);

public sealed class ArchiveService(IArchivedProgrammeRepository archive)
{
    public async Task<ServiceResult<ArchiveForgotten>> ForgetServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => ServiceResult<ArchiveForgotten>.Success(new ArchiveForgotten(
            await archive.ForgetServiceAsync(networkId, serviceId, cancellationToken)));
}
