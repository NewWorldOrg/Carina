using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed class RepositoryDatabase : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_repository_test";
    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    private readonly string connectionString = Scratch();

    public async Task InitializeAsync()
    {
        await using (CarinaDbContext dropping = Open())
        {
            await dropping.Database.EnsureDeletedAsync();
        }

        await MakeTheDatabaseAndWhatItsConstraintsCallAsync();

        await using CarinaDbContext context = Open();
        await context.Database.EnsureCreatedAsync();
    }

    private async Task MakeTheDatabaseAndWhatItsConstraintsCallAsync()
    {
        string maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }
            .ConnectionString;

        await using (var server = new NpgsqlConnection(maintenance))
        {
            await server.OpenAsync();
            await using var creating = new NpgsqlCommand($"CREATE DATABASE {ScratchDatabase}", server);
            await creating.ExecuteNonQueryAsync();
        }

        await using var scratch = new NpgsqlConnection(connectionString);
        await scratch.OpenAsync();
        await using var declaring = new NpgsqlCommand(RecordingGuards.Functions, scratch);
        await declaring.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public CarinaDbContext Open()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(connectionString);

        return new CarinaDbContext(builder.Options);
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
