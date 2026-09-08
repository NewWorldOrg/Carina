using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db;

public static class DbEntryPoint
{
    public const int SuccessExitCode = 0;
    public const int MigrationFailedExitCode = 1;
    public const int UsageExitCode = 64;
    public const int UnusableConfigurationExitCode = 78;
    public const int SourceUnreadableExitCode = 69;
    public const int CarryFailedExitCode = 70;

    public const string Usage = """
        usage: Carina.Db --migrate
               Carina.Db --carry --from <source directory> --into <new root directory> [--for-real]
        """;

    public static Task<int> RunAsync(string[] args, TextWriter error) => RunAsync(args, error, error);

    public static async Task<int> RunAsync(string[] args, TextWriter error, TextWriter output)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        if (args is [CarryArguments.Verb, ..])
        {
            return await CarryAsync(args, error, output);
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

    private static async Task<int> CarryAsync(string[] args, TextWriter error, TextWriter output)
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

        if (!MigrationSourceSettings.TryRead(
                Environment.GetEnvironmentVariable,
                out MigrationSourceSettings? source,
                out string unreachable))
        {
            await error.WriteLineAsync(unreachable);

            return UnusableConfigurationExitCode;
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

        await using (context)
        {
            return await CarriedAsync(carry, source, context, error, output);
        }
    }

    private static async Task<int> CarriedAsync(
        CarryArguments carry,
        MigrationSourceSettings source,
        CarinaDbContext context,
        TextWriter error,
        TextWriter output)
    {
        MigrationRecordRepository records = new(context);

        try
        {
            MigrationPassage passage = new(
                new MigrationSourceLedgerReader(new MySqlMigrationSourceConnection(source)),
                new LocalMigrationSourceDirectory(carry.From),
                new MigrationCarriage(
                    new HardLinkMigrationCarrier(carry.From, carry.Into, carry.Root),
                    new RecordingRepository(context),
                    new RuleRepository(context),
                    new EncodeJobRepository(context),
                    new EncodeDestinationRepository(context),
                    new EncodeProfileRepository(context),
                    TimeProvider.System),
                records,
                new MigrationLease(context),
                TimeProvider.System);

            MigrationRunId id = await passage.RunAsync(
                carry.Pass,
                await RescannedAsync(context, CancellationToken.None),
                CancellationToken.None);

            MigrationReport read = await records.ReadAsync(id, CancellationToken.None)
                ?? throw new MigrationUnclassifiedException(
                    "The run finished and nothing was written down about it.");

            await output.WriteAsync(CarrySaid.Of(read));

            return SuccessExitCode;
        }
        catch (MigrationSourceUnreadableException unreadable)
        {
            await error.WriteLineAsync(
                $"{unreadable.Message} Nothing was carried and nothing was written down.");

            return SourceUnreadableExitCode;
        }
        catch (Exception refused)
            when (refused is MigrationAlreadyRunningException
                or MigrationCarryRefusedException
                or MigrationUnclassifiedException)
        {
            await error.WriteLineAsync(refused.Message);

            return CarryFailedExitCode;
        }
        catch (Exception failure)
        {
            await error.WriteLineAsync($"Carina.Db {CarryArguments.Verb} failed: {Describe(failure)}");

            return CarryFailedExitCode;
        }
    }

    private static async Task<IReadOnlyList<RescannedService>> RescannedAsync(
        CarinaDbContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BroadcastService> rescanned =
            await new BroadcastServiceRepository(context).ListAsync(cancellationToken);

        return
        [
            .. rescanned.Select(service =>
                new RescannedService(new ServiceKey(service.NetworkId, service.ServiceId), service.Name)),
        ];
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
