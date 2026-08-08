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

    // Time is never read from the ambient clock: everything that needs "now" takes
    // this abstraction so recording windows can be driven deterministically in tests.
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
