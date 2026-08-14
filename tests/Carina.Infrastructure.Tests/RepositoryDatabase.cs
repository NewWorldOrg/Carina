using Carina.Infrastructure.Persistence;

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
        await using var context = Open();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
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
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
