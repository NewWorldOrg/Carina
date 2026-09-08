using System.Globalization;

using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db;

public static class DbEntryPoint
{
    public const int SuccessExitCode = 0;
    public const int MigrationFailedExitCode = 1;
    public const int UsageExitCode = 64;
    public const int UnusableConfigurationExitCode = 78;
    public const int SourceUnreadableExitCode = 69;

    public const string Usage = """
        usage: Carina.Db --migrate
               Carina.Db --carry --from <source directory> --into <new root directory> [--for-real]
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter error)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        if (args is [CarryArguments.Verb, ..])
        {
            return await CarryAsync(args, error);
        }

        if (args is not ["--migrate"])
        {
            await error.WriteLineAsync(Usage);

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
                await using MigrationLock lease = await MigrationLock.TakeAsync(context, error);

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

    private static async Task<int> CarryAsync(string[] args, TextWriter error)
    {
        if (!CarryArguments.TryRead(args, out CarryArguments? carry, out string problem))
        {
            await error.WriteLineAsync(problem);
            await error.WriteLineAsync(Usage);

            return UsageExitCode;
        }

        if (!Directory.Exists(carry.From))
        {
            await error.WriteLineAsync(
                $"The output directory of the system being replaced, '{carry.From}', is not there.");

            return UnusableConfigurationExitCode;
        }

        if (!Directory.Exists(carry.Into))
        {
            await error.WriteLineAsync(
                $"The new root '{carry.Into}' is not there. Make it on the same filesystem as the source, "
                + "writable by the application and by nothing else, and run this again.");

            return UnusableConfigurationExitCode;
        }

        await error.WriteLineAsync(
            "Nothing reads the ledger of the system being replaced yet, so there is nothing to read a run from. "
            + "Nothing was carried and nothing was written down.");

        return SourceUnreadableExitCode;
    }

    private static string Describe(Exception exception)
    {
        Exception innermost = exception;

        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        return ReferenceEquals(innermost, exception)
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{exception.GetType().Name}: {exception.Message} ({innermost.GetType().Name}: {innermost.Message})";
    }
}
