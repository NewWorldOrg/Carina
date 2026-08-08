using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Carina.Db;

/// <summary>
/// Design-time context factory used by the EF Core tooling and by the migration entry point.
/// </summary>
public sealed class CarinaDbContextFactory : IDesignTimeDbContextFactory<CarinaDbContext>
{
    /// <summary>Environment variable carrying the PostgreSQL connection string.</summary>
    public const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    /// <inheritdoc />
    public CarinaDbContext CreateDbContext(string[] args)
        => Create(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    /// <summary>
    /// Creates a context for the given connection string, failing fast with a
    /// diagnostic message when it is missing.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public static CarinaDbContext Create(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string: expected environment variable {ConnectionStringVariable} to be set, but it was empty.");
        }

        var options = new DbContextOptionsBuilder<CarinaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CarinaDbContext(options);
    }
}
