extern alias db;

using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Auth;

using db::Carina.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests;

/// <summary>
/// A scratch PostgreSQL database of its own made by every migration, for the statements that read what only the
/// migrations make, such as the view the recorder reads its ticks from and the defaults older rows were given. The
/// container is the one the infrastructure builds over it. Each class that takes it gets a database of its own, which
/// is dropped when the class is done.
/// </summary>
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

    public ServiceProvider Infrastructure() => RepositoryDatabase.Container(connectionString, SealingKeys.Fresh(), _ => { });

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
