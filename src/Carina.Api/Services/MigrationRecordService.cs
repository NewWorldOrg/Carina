using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Migration;

namespace Carina.Api.Services;

public sealed record MigrationRecordRead(MigrationRecordSummary? Summary, PaginatedList<MigrationDetail> Details);

public sealed class MigrationRecordService(IMigrationRecordRepository records)
{
    public async Task<ServiceResult<MigrationRecordRead>> ReadAsync(
        MigrationDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        MigrationRecordSummary? summary = await records.SummariseAsync(cancellationToken);

        return ServiceResult<MigrationRecordRead>.Success(new MigrationRecordRead(
            summary,
            summary is null
                ? new PaginatedList<MigrationDetail>([], 0, query.Page, query.PerPage)
                : await records.ListDetailsAsync(summary.Run.Id, query, cancellationToken)));
    }
}
