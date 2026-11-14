using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Persistence;

internal static class MigrationScratch
{
    private const string ScratchDatabase = "carina_migration_test";

    public static string ConnectionString()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
