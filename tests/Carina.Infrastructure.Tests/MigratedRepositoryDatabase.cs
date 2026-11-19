extern alias db;

using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Auth;

using db::Carina.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed class MigratedRepositoryDatabase : IAsyncLifetime
{
    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    private readonly string connectionString = Scratch($"carina_migrated_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(connectionString);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using CarinaDbContext context = CarinaDbContextFactory.Create(connectionString);
        await context.Database.EnsureDeletedAsync();
    }

    public ServiceProvider Infrastructure() => Infrastructure(_ => { });

    public ServiceProvider Infrastructure(Action<IServiceCollection> replace)
        => RepositoryDatabase.Container(connectionString, SealingKeys.Fresh(), replace);

    public async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();

        return connection;
    }

    private static string Scratch(string database)
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = database }.ConnectionString;
    }
}
