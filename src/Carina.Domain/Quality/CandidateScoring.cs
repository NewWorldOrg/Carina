using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public sealed record CandidateScored(CandidateChannelId Candidate, CandidateScore Score);

public static class CandidateScoring
{
    public static IReadOnlyList<CandidateScored> Over(
        IReadOnlyList<CandidateChannel> candidates,
        IReadOnlyList<IntendedStream> intended,
        IReadOnlyList<QualitySignalWindow> windows,
        QualityPeriod period,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(intended);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(period);
        UtcTimes.Required(at, nameof(at));

        List<CandidateScored> scored = [];

        foreach (CandidateChannel candidate in candidates)
        {
            if (Scored(candidate, candidates, intended, windows, period, at) is { } score)
            {
                scored.Add(new CandidateScored(candidate.Id, score));
            }
        }

        return scored;
    }

    private static CandidateScore? Scored(
        CandidateChannel candidate,
        IReadOnlyList<CandidateChannel> candidates,
        IReadOnlyList<IntendedStream> intended,
        IReadOnlyList<QualitySignalWindow> windows,
        QualityPeriod period,
        DateTime at)
    {
        if (SignalFiling.StreamFor(intended, candidate.Tuning) is not { } stream
            || !stream.NetworkId.Equals(candidate.NetworkId))
        {
            return null;
        }

        CandidateChannel[] carriers =
        [
            .. candidates.Where(held => held.IsSelected
                                        && held.NetworkId.Equals(stream.NetworkId)
                                        && stream.Services.Contains(held.ServiceId)),
        ];

        if (carriers.Length is 0 || carriers.Any(carrier => !SignalFiling.Same(carrier.Tuning, candidate.Tuning)))
        {
            return null;
        }

        DateTime selectedSince = carriers.Max(carrier => carrier.SelectedAt.GetValueOrDefault());
        DateTime from = selectedSince > period.From ? selectedSince : period.From;

        if (from >= period.Until)
        {
            return null;
        }

        ServiceId filedUnder = stream.Services[0];

        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
        [
            .. windows.Where(window => window.Network.Equals(stream.NetworkId)
                                       && window.Service.Equals(filedUnder)
                                       && window.Start >= from
                                       && window.Start < period.Until),
        ]);

        long taken = figures.Sum(figure => figure.Taken);

        if (taken <= 0)
        {
            return null;
        }

        int[] carrierToNoise = [.. figures.Select(figure => figure.CarrierToNoiseLowest).OfType<int>()];
        double[] errorRates = [.. figures.Select(figure => figure.BitErrorRateHighest).OfType<double>()];

        return CandidateScore.Of(
            taken,
            figures.Sum(figure => figure.Locked),
            carrierToNoise.Length is 0 ? null : carrierToNoise.Min(),
            errorRates.Length is 0 ? null : errorRates.Max(),
            from,
            period.Until,
            at);
    }
}
