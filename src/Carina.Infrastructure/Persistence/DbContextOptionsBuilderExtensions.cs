using Microsoft.EntityFrameworkCore;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Carina.Infrastructure.Persistence;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseCarinaDatabase(
        this DbContextOptionsBuilder builder,
        string? connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
        => builder
            .UseNpgsql(connectionString, configureNpgsql)
            .UseSnakeCaseNamingConvention();
}
