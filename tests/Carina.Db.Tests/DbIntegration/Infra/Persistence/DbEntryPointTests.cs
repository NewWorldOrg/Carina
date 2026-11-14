using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Persistence;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DbEntryPointTests
{
    [Fact]
    public async Task EntryPointMigratesRepeatablyAndFailsLoudlyOnBadCredentials()
    {
        string connectionString = MigrationScratch.ConnectionString();

        using (new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, connectionString))
        {
            Assert.Equal(DbEntryPoint.SuccessExitCode, await DbEntryPoint.RunAsync(["--migrate"], new StringWriter()));
            Assert.Equal(DbEntryPoint.SuccessExitCode, await DbEntryPoint.RunAsync(["--migrate"], new StringWriter()));
        }

        string wrongPassword = new NpgsqlConnectionStringBuilder(connectionString) { Password = "wrong" }.ConnectionString;

        using (new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, wrongPassword))
        {
            var error = new StringWriter();

            int exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

            Assert.Equal(DbEntryPoint.MigrationFailedExitCode, exitCode);
            Assert.Contains("Carina.Db --migrate failed", error.ToString(), StringComparison.Ordinal);
        }
    }
}
