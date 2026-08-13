using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private const string ConnectionString = "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder";

    [Fact]
    public void RegistersThePersistenceContext()
    {
        using var provider = new ServiceCollection()
            .AddCarinaInfrastructure(ConnectionString)
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CarinaDbContext>());
    }

    [Fact]
    public void RegistersTheTimeProvider()
    {
        using var provider = new ServiceCollection()
            .AddCarinaInfrastructure(ConnectionString)
            .BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void RejectsAnEmptyConnectionString()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCarinaInfrastructure("  "));
    }
}
