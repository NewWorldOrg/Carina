using Carina.Domain.Channels;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Logos;

public sealed record LogoRoundResult(int Visited, int LeftForTheNextSweep);

public sealed class LogoRound(
    ILogoVisitRepository visits,
    LogoVisitor visitor,
    LogoWriter writer,
    LogoSweepSettings settings,
    TimeProvider clock,
    ILogger<LogoRound> logger)
{
    public async Task<LogoRoundResult> WalkAsync(
        IReadOnlyList<BroadcastStream> due,
        CancellationToken interruption,
        CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(due);

        using CancellationTokenSource walking = CancellationTokenSource.CreateLinkedTokenSource(
            interruption,
            abort);
        DateTimeOffset began = clock.GetUtcNow();
        int visited = 0;

        foreach (BroadcastStream transport in due)
        {
            if (walking.IsCancellationRequested)
            {
                break;
            }

            if (!LogoRotation.ThereIsRoomForAnotherVisit(settings, clock.GetUtcNow() - began, visited))
            {
                logger.LogInformation(
                    "A logo sweep spent its {Budget} on {Visited} transport(s); {Left} more wait for the next sweep.",
                    settings.RoundBudget,
                    visited,
                    due.Count - visited);

                break;
            }

            LogoVisitResult visit = await VisitAsync(transport, walking.Token, abort);

            if (visit.WorthWaitingOut)
            {
                logger.LogInformation("Every tuner stayed busy; the logo sweep waits for the next round.");

                break;
            }

            await KeepAsync(transport, visit, abort);

            visited++;
        }

        return new LogoRoundResult(visited, due.Count - visited);
    }

    private async Task<LogoVisitResult> VisitAsync(
        BroadcastStream transport,
        CancellationToken walking,
        CancellationToken abort)
    {
        try
        {
            return await visitor.VisitAsync(transport, walking);
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            return LogoVisitResult.NothingCameOfIt(LogoVisitOutcome.Interrupted);
        }
    }

    private async Task KeepAsync(BroadcastStream transport, LogoVisitResult visit, CancellationToken abort)
    {
        LogosWritten written = await writer.WriteAsync(visit, abort);

        await visits.RecordAsync(
            transport.NetworkId,
            transport.TransportStreamId,
            visit.Outcome,
            clock.GetUtcNow().UtcDateTime,
            abort);

        logger.LogInformation(
            "A logo sweep of {NetworkId}-{TransportStreamId} ended as {Outcome} with {Pictures} picture(s)"
            + " for {Stations} station(s), and {NoPicture} station(s) that broadcast none.",
            transport.NetworkId.Value,
            transport.TransportStreamId.Value,
            visit.Outcome,
            written.Pictures,
            written.Stations,
            written.NoPicture);
    }
}
