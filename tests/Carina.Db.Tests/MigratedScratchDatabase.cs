using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

/// <summary>
/// A database made by every migration, with the built-in version of the model kept as the application keeps it when it
/// starts, at <see cref="Installed"/>.
/// </summary>
public sealed class MigratedScratchDatabase : IAsyncLifetime
{
    public static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string ScratchDatabase = "carina_channel_scan_test";

    public string ConnectionString { get; } = Scratch();

    public async Task InitializeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(ConnectionString);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await new SegmentModelVersionRepository(context).KeepBuiltInAsync(SegmentModel.BuiltIn, Installed, CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
