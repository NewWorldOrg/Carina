using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Carina.Db;

public sealed class CarinaDbContextFactory : IDesignTimeDbContextFactory<CarinaDbContext>
{
    public const string ConnectionStringVariable = "CARINA_DB_CONNECTION";
    public const string MigrationsAssemblyName = "Carina.Db";

    public CarinaDbContext CreateDbContext(string[] args)
        => Create(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    public static CarinaDbContext Create(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No database connection string: expected environment variable {ConnectionStringVariable} to be set, but it was empty.");
        }

        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssemblyName));

        return new CarinaDbContext(builder.Options);
    }
}
