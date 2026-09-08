using System.Data.Common;

namespace Carina.Infrastructure.Migration;

public interface IMigrationSourceConnection
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}
