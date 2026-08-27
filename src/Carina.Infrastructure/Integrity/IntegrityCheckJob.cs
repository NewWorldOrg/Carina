using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Integrity;

public sealed class IntegrityCheckJob(
    IServiceScopeFactory scopes,
    IRecordingFileSurvey survey,
    IntegritySettings settings,
    TimeProvider clock,
    ILogger<IntegrityCheckJob> logger) : BackgroundService
{
    private readonly Lock gate = new();

    private IntegrityCheckId? running;

    public IntegrityCheckId? RunningCheck
    {
        get
        {
            lock (gate)
            {
                return running;
            }
        }
    }

    public async Task<IntegrityRun> RunAsync(CancellationToken cancellationToken)
    {
        IntegrityCheckId asked = IntegrityCheckId.New();

        lock (gate)
        {
            if (running is { } walking)
            {
                return IntegrityRun.RefusedBecauseOneIsRunning(walking);
            }

            running = asked;
        }

        try
        {
            return IntegrityRun.Of(await SweepAsync(asked, cancellationToken));
        }
        finally
        {
            lock (gate)
            {
                running = null;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.WalksAnything)
        {
            logger.LogWarning(
                "No output root is mounted into this process, so the ledger is never checked against the files.");

            return;
        }

        TimeSpan waiting = settings.BeforeFirstSweep;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(waiting, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waiting = settings.BetweenSweeps;

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A ledger check failed; the next one is unaffected.");
            }
        }
    }

    private async Task<IntegrityReport> SweepAsync(IntegrityCheckId id, CancellationToken cancellationToken)
    {
        DateTime startedAt = clock.GetUtcNow().UtcDateTime;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IReadOnlyList<LedgerFile> ledger = await scope.ServiceProvider
            .GetRequiredService<IRecordingLedger>()
            .ListAsync(cancellationToken);

        IReadOnlyList<OutputRoot> roots = await survey.RootsAsync(cancellationToken);
        List<RootListing> listings = [];

        foreach (OutputRoot root in Walked(roots, ledger))
        {
            listings.Add(await survey.ListAsync(root, cancellationToken));
        }

        IntegrityReport swept = IntegrityScan.Compare(
            id,
            ledger,
            listings,
            startedAt,
            clock.GetUtcNow().UtcDateTime);

        await scope.ServiceProvider
            .GetRequiredService<IIntegrityCheckRepository>()
            .SaveAsync(swept, cancellationToken);

        logger.LogInformation(
            "A ledger check read {Rows} row(s) and {Files} file(s) across {Roots} output root(s) "
            + "and found {Findings} disagreement(s).",
            swept.Check.LedgerRowsRead,
            swept.Check.FilesRead,
            swept.Check.RootsWalked,
            swept.Findings.Count);

        return swept;
    }

    private static IEnumerable<OutputRoot> Walked(
        IReadOnlyList<OutputRoot> offered,
        IReadOnlyList<LedgerFile> ledger)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<OutputRoot> walked = [];

        foreach (OutputRoot root in offered.Concat(ledger.Select(row => row.Root)))
        {
            if (seen.Add(root.Value))
            {
                walked.Add(root);
            }
        }

        return walked;
    }
}
