using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Carina.Db;

public sealed class CarinaDbContextFactory : IDesignTimeDbContextFactory<CarinaDbContext>
{
    public const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    public CarinaDbContext CreateDbContext(string[] args)
        => Create(Environment.GetEnvironmentVariable(ConnectionStringVariable));

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
