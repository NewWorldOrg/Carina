using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db;

public static class DbEntryPoint
{
    public const int SuccessExitCode = 0;
    public const int MigrationFailedExitCode = 1;
    public const int UsageExitCode = 64;
    public const int UnusableConfigurationExitCode = 78;

    public static async Task<int> RunAsync(string[] args, TextWriter error)
    {
        if (args is not ["--migrate"])
        {
            await error.WriteLineAsync("usage: Carina.Db --migrate");
            return UsageExitCode;
        }

        CarinaDbContext context;

        try
        {
            context = new CarinaDbContextFactory().CreateDbContext(args);
        }
        catch (InvalidOperationException unusable)
        {
            await error.WriteLineAsync(unusable.Message);
            return UnusableConfigurationExitCode;
        }

        try
        {
            await using (context)
            {
                await using var lease = await MigrationLock.TakeAsync(context, error);

                await context.Database.MigrateAsync();
            }

            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(
                $"Carina.Db --migrate failed: {Describe(exception)}");
            return MigrationFailedExitCode;
        }
    }

    private static string Describe(Exception exception)
    {
        var innermost = exception;

        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        return ReferenceEquals(innermost, exception)
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{exception.GetType().Name}: {exception.Message} ({innermost.GetType().Name}: {innermost.Message})";
    }
}
