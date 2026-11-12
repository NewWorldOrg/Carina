extern alias db;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using db::Carina.Db;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace Carina.Api.Tests.DbIntegration;

/// <summary>
/// A scratch PostgreSQL database made by every migration, with the built-in version of the model kept as the
/// application keeps it when it starts, and the application raised over it with no background work, so a test can
/// take an application service from the container the application builds.
/// </summary>
public sealed class AppDatabase : IAsyncLifetime
{
    public static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string ScratchDatabase = "carina_app_service_test";

    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    public string ConnectionString { get; } = Scratch();

    public async Task InitializeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(ConnectionString);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await new SegmentModelVersionRepository(context).KeepBuiltInAsync(SegmentModel.BuiltIn, Installed, CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
