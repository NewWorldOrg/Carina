using System.Data.Common;

using MySqlConnector;

namespace Carina.Infrastructure.Migration;

public sealed class MySqlMigrationSourceConnection(MigrationSourceSettings settings) : IMigrationSourceConnection
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MySqlConnection connection = new(settings.ConnectionAsSupplied);

        try
        {
            await connection.OpenAsync(cancellationToken);

            return connection;
        }
        catch (Exception refused) when (refused is not OperationCanceledException)
        {
            await connection.DisposeAsync();

            throw new MigrationSourceUnreadableException(
                "The system being replaced could not be reached with what "
                + $"{MigrationSourceSettings.ConnectionVariable} holds ({refused.GetType().Name}).");
        }
    }
}
