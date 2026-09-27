using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SessionHandleKeyTests
{
    private const string ScratchDatabase = "carina_session_handle_key_test";

    private const string BeforeTheHash = "20260926103402_ReservationConcurrency";

    private const string TheHash = "20260926141610_SessionsAreKeptAsHashes";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly IReadOnlyList<SessionId> Carried =
    [
        .. Enumerable.Range(0, 32).Select(index => new SessionId(Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(index.ToString(CultureInfo.InvariantCulture)))))),
    ];

    [Fact]
    public async Task EverySessionHeldByItsCookieBeforeTheMigrationIsHeldByTheHashOfItAfterward()
    {
        await using CarinaDbContext context = await HeldByTheirCookiesAsync();

        await context.GetService<IMigrator>().MigrateAsync(TheHash, Cancel);

        List<string> handles = await context.Database
            .SqlQueryRaw<string>("SELECT handle AS \"Value\" FROM auth_session")
            .ToListAsync(Cancel);

        Assert.Equal(
            Carried.Select(cookie => SessionHandle.Of(cookie).Value).Order(StringComparer.Ordinal),
            handles.Order(StringComparer.Ordinal));
        Assert.Contains(handles, handle => handle.Contains('-', StringComparison.Ordinal));
        Assert.Contains(handles, handle => handle.Contains('_', StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingACookieCarriedIsLeftInTheTableAfterTheMigration()
    {
        await using CarinaDbContext context = await HeldByTheirCookiesAsync();

        await context.GetService<IMigrator>().MigrateAsync(TheHash, Cancel);

        List<string> rows = await context.Database
            .SqlQueryRaw<string>("SELECT row_to_json(held)::text AS \"Value\" FROM auth_session AS held")
            .ToListAsync(Cancel);

        Assert.Equal(Carried.Count, rows.Count);
        Assert.DoesNotContain(
            rows,
            row => Carried.Any(cookie => row.Contains(cookie.Value, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ASessionHeldBeforeTheMigrationIsStillFoundByTheCookieItsBrowserCarries()
    {
        await using (CarinaDbContext migrating = await HeldByTheirCookiesAsync())
        {
            await migrating.GetService<IMigrator>().MigrateAsync(TheHash, Cancel);
        }

        await using CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch());
        var sessions = new AuthSessionRepository(reading);

        foreach (SessionId cookie in Carried)
        {
            AuthSession? found = await sessions.FindAsync(SessionHandle.Of(cookie), Cancel);

            Assert.NotNull(found);
            Assert.Equal("a device", found.DeviceLabel);
        }
    }

    [Fact]
    public async Task RollingTheMigrationBackForgetsEverySessionRatherThanHandingHashesToCodeThatReadsCookies()
    {
        await using CarinaDbContext context = await HeldByTheirCookiesAsync();
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(TheHash, Cancel);
        await migrator.MigrateAsync(BeforeTheHash, Cancel);

        List<int> counted = await context.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM auth_session")
            .ToListAsync(Cancel);

        Assert.Equal([0], counted);
    }

    private static async Task<CarinaDbContext> HeldByTheirCookiesAsync()
    {
        CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(BeforeTheHash, Cancel);

        await using NpgsqlConnection connection = await OpenAsync();

        foreach (SessionId cookie in Carried)
        {
            await using var inserting = new NpgsqlCommand(
                """
                INSERT INTO auth_session (id, subject, display_name, method, created_at, last_used_at, device_label)
                VALUES (@id, 'carina', 'carina', 'Local', now(), now(), 'a device')
                """,
                connection);
            inserting.Parameters.AddWithValue("id", cookie.Value);
            await inserting.ExecuteNonQueryAsync(Cancel);
        }

        return context;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
        await connection.OpenAsync(Cancel);

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
