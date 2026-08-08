using Microsoft.EntityFrameworkCore;

namespace Carina.Db.Tests;

public sealed class CarinaDbContextFactoryTests
{
    [Fact]
    public void CreatesAPostgresContextFromTheConnectionString()
    {
        using var context = CarinaDbContextFactory.Create(
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    // Configuration errors have to surface at startup with a message naming the
    // missing setting, not as a connection failure somewhere deeper.
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
