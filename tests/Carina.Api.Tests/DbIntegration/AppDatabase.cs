using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace Carina.Api.Tests.DbIntegration;

/// <summary>
/// A scratch PostgreSQL database made from the model of the application, and the application raised over it
/// with no background work, so a test can take an application service from the container the application builds.
/// </summary>
public sealed class AppDatabase : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_app_service_test";

    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    public string ConnectionString { get; } = Scratch();

    public async Task InitializeAsync()
    {
        await using (CarinaDbContext dropping = Open())
        {
            await dropping.Database.EnsureDeletedAsync();
        }

        string maintenance = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" }.ConnectionString;

        await using (NpgsqlConnection server = new(maintenance))
        {
            await server.OpenAsync();

            await using NpgsqlCommand creating = new($"CREATE DATABASE {ScratchDatabase}", server);
            await creating.ExecuteNonQueryAsync();
        }

        await RunAsync(RecordingGuards.Functions);

        await using (CarinaDbContext context = Open())
        {
            await context.Database.EnsureCreatedAsync();
        }

        await RunAsync(RecordingGuards.Projection, RecordingGuards.Immutability);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public CarinaDbContext Open()
    {
        DbContextOptionsBuilder<CarinaDbContext> builder = new();
        builder.UseCarinaDatabase(ConnectionString);

        return new CarinaDbContext(builder.Options);
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    /// <summary>
    /// Raises the application over this database, reading the time from <paramref name="clock"/>, with what
    /// <paramref name="also"/> changes in its container.
    /// </summary>
    public RaisedApplication Application(TimeProvider clock, string publicOrigin, Action<IServiceCollection>? also = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        TestingWebApplicationFactory root = new();
        WebApplicationFactory<Program> built = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TestingWebApplicationFactory.ConnectionStringKey, ConnectionString);
            builder.UseSetting(PublicOrigin.Key, publicOrigin);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton(clock);
                also?.Invoke(services);
            });
        });

        return new RaisedApplication(root, built);
    }

    private async Task RunAsync(params string[] statements)
    {
        await using NpgsqlConnection scratch = await OpenConnectionAsync();

        foreach (string sql in statements)
        {
            await using NpgsqlCommand running = new(sql, scratch);
            await running.ExecuteNonQueryAsync();
        }
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
