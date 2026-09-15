using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Infrastructure.Quality;

public sealed class CandidateScoreRound(
    ICandidateChannelRepository candidates,
    IBroadcastStreamDirectory streams,
    IQualitySignalReader signals,
    QualitySignalSettings settings,
    TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;

        if (QualityPeriod.Of(at - settings.EvaluateCandidatesOver, at, at) is not { } period)
        {
            return 0;
        }

        IReadOnlyList<CandidateChannel> held = await candidates.ListAllAsync(cancellationToken);
        IReadOnlyList<IntendedStream> intended = await streams.ListIntendedAsync(cancellationToken);
        IReadOnlyList<QualitySignalWindow> windows = await signals.WindowsAsync(period, cancellationToken);

        int written = 0;

        foreach (CandidateScored scored in CandidateScoring.Over(held, intended, windows, period, at))
        {
            if (await candidates.ScoreAsync(scored.Candidate, scored.Score, cancellationToken))
            {
                written++;
            }
        }

        return written;
    }
}
