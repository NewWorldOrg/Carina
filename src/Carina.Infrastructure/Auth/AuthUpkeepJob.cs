using Carina.Domain.Auth;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Auth;

public sealed class AuthUpkeepJob(
    IServiceScopeFactory scopes,
    IOidcReachability reachability,
    SessionPolicy policy,
    TimeProvider clock,
    ILogger<AuthUpkeepJob> logger) : BackgroundService
{
    public static TimeSpan BetweenRounds { get; } = TimeSpan.FromMinutes(5);

    public async Task ProbeOnceAsync(
        IOidcSettingsRepository settings,
        IOidcDirectory directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(directory);

        OidcSettings? held = await settings.FindAsync(cancellationToken);

        if (held?.IsConfigured is not true)
        {
            reachability.Record(OidcReach.NotConfigured);

            return;
        }

        if (await directory.ProbeAsync(held, cancellationToken) is null)
        {
            logger.LogWarning(
                "The identity provider's discovery document could not be read, so signing in through it is "
                + "degraded until it answers again. The local account still signs in.");
        }
    }

    public async Task<int> ForgetEndedSessionsAsync(
        IAuthSessionRepository sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<AuthSession> held = await sessions.ListAllAsync(cancellationToken);
        AuthSession[] ended = [.. held.Where(session => session.CanBeForgotten(now, policy))];

        if (ended.Length is 0)
        {
            return 0;
        }

        int forgotten = await sessions.ForgetAsync(ended, cancellationToken);

        logger.LogInformation(
            "{Forgotten} session(s) that nobody can sign in with any more were forgotten.",
            forgotten);

        return forgotten;
    }

    public async Task UpkeepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            await ProbedAsync(scope.ServiceProvider, cancellationToken);
            await ForgottenAsync(scope.ServiceProvider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A round of authentication upkeep could not be started.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var pace = new PeriodicTimer(BetweenRounds, clock);

        do
        {
            await UpkeepOnceAsync(stoppingToken);
        }
        while (await WaitAsync(pace, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer pace, CancellationToken stoppingToken)
    {
        try
        {
            return await pace.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ProbedAsync(IServiceProvider serving, CancellationToken stoppingToken)
    {
        try
        {
            await ProbeOnceAsync(
                serving.GetRequiredService<IOidcSettingsRepository>(),
                serving.GetRequiredService<IOidcDirectory>(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "The identity provider could not be probed, so signing in through it stays degraded.");
        }
    }

    private async Task ForgottenAsync(IServiceProvider serving, CancellationToken stoppingToken)
    {
        try
        {
            await ForgetEndedSessionsAsync(
                serving.GetRequiredService<IAuthSessionRepository>(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "Sessions that had ended could not be forgotten; the next round is unaffected.");
        }
    }
}
