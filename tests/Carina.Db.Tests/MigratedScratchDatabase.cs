using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

public sealed class MigratedScratchDatabase : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_channel_scan_test";

    public string ConnectionString { get; } = Scratch();

    public async Task InitializeAsync()
    {
        await using var context = CarinaDbContextFactory.Create(ConnectionString);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
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
        var configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
