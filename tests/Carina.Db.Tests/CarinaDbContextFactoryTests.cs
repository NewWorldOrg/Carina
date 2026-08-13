using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Carina.Db.Tests;

public sealed class CarinaDbContextFactoryTests
{
    private const string ConnectionString = "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder";

    [Fact]
    public void CreatesAPostgresContextFromTheConnectionString()
    {
        using var context = CarinaDbContextFactory.Create(ConnectionString);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void KeepsMigrationsInTheDbProject()
    {
        using var context = CarinaDbContextFactory.Create(ConnectionString);

        var relational = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();

        Assert.Equal("Carina.Db", relational.MigrationsAssembly);
    }

    [Fact]
    public void AppliesTheSnakeCaseNamingConvention()
    {
        using var context = CarinaDbContextFactory.Create(ConnectionString);

        Assert.Contains(
            context.GetService<IDbContextOptions>().Extensions,
            extension => extension.GetType().Name == "NamingConventionsOptionsExtension");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FailsFastWhenTheConnectionStringIsMissing(string? connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CarinaDbContextFactory.Create(connectionString));

        Assert.Contains(CarinaDbContextFactory.ConnectionStringVariable, exception.Message, StringComparison.Ordinal);
    }
}
