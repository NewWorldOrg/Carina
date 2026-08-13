using Microsoft.EntityFrameworkCore;

namespace Carina.Db;

public static class DbEntryPoint
{
    public const int SuccessExitCode = 0;
    public const int MigrationFailedExitCode = 1;
    public const int UsageExitCode = 64;

    public static async Task<int> RunAsync(string[] args, TextWriter error)
    {
        if (args is not ["--migrate"])
        {
            await error.WriteLineAsync("usage: Carina.Db --migrate");
            return UsageExitCode;
        }

        try
        {
            await using var context = new CarinaDbContextFactory().CreateDbContext(args);
            await context.Database.MigrateAsync();
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(
                $"Carina.Db --migrate failed: {exception.GetType().Name}: {exception.Message}");
            return MigrationFailedExitCode;
        }
    }
}
