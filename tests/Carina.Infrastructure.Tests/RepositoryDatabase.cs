using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Tests.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed class RepositoryDatabase : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_repository_test";
    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    private readonly string connectionString = Scratch();

    public async Task InitializeAsync()
    {
        await using (CarinaDbContext dropping = Open())
        {
            await dropping.Database.EnsureDeletedAsync();
        }

        await MakeTheDatabaseAndWhatItsConstraintsCallAsync();

        await using CarinaDbContext context = Open();
        await context.Database.EnsureCreatedAsync();

        await RunAsync(RecordingGuards.Projection, RecordingGuards.Immutability);
    }

    private async Task MakeTheDatabaseAndWhatItsConstraintsCallAsync()
    {
        string maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }
            .ConnectionString;

        await using (var server = new NpgsqlConnection(maintenance))
        {
            await server.OpenAsync();

            await using var asking = new NpgsqlCommand(
                $"SELECT count(*) FROM pg_database WHERE datname = '{ScratchDatabase}'",
                server);

            if ((long)(await asking.ExecuteScalarAsync())! is 0)
            {
                await using var creating = new NpgsqlCommand($"CREATE DATABASE {ScratchDatabase}", server);
                await creating.ExecuteNonQueryAsync();
            }
        }

        await RunAsync(RecordingGuards.Functions);
    }

    private async Task RunAsync(params string[] statements)
    {
        await using var scratch = new NpgsqlConnection(connectionString);
        await scratch.OpenAsync();

        foreach (string sql in statements)
        {
            await using var running = new NpgsqlCommand(sql, scratch);
            await running.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public CarinaDbContext Open(params IInterceptor[] interceptors)
    {
        ArgumentNullException.ThrowIfNull(interceptors);

        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(connectionString);

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new CarinaDbContext(builder.Options);
    }

    public ServiceProvider Infrastructure() => Infrastructure(SealingKeys.Fresh());

    public ServiceProvider Infrastructure(DirectoryInfo sealingKeys) => Infrastructure(sealingKeys, _ => { });

    /// <summary>
    /// The container the infrastructure builds over the scratch database, with what <paramref name="replace"/>
    /// registers last standing in for what the infrastructure registered.
    /// </summary>
    public ServiceProvider Infrastructure(Action<IServiceCollection> replace) => Infrastructure(SealingKeys.Fresh(), replace);

    public ServiceProvider Infrastructure(DirectoryInfo sealingKeys, Action<IServiceCollection> replace)
    {
        ArgumentNullException.ThrowIfNull(sealingKeys);
        ArgumentNullException.ThrowIfNull(replace);

        IServiceCollection services = new ServiceCollection()
            .AddLogging()
            .AddCarinaInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Carina"] = connectionString,
                    ["CARINA_DRIVER_SOCKET"] = "/run/carina/driver.sock",
                    [SealingKeyOptions.DirectoryKey] = sealingKeys.FullName,
                })
                .Build());
        replace(services);

        return services.BuildServiceProvider();
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
