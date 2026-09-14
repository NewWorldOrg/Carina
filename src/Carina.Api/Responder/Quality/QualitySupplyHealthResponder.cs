using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualitySupplyResponder(SupplySilence Silence, int Watched, int Quiet, QualityState State);

public sealed record QualitySupplyHealthResponder(
    DateTime ReadAt,
    double AppliedValue,
    bool AppliedProvisional,
    bool TunersWereAsked,
    IReadOnlyList<QualitySupplyResponder> Supplies)
{
    public static QualitySupplyHealthResponder Of(SupplyStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new QualitySupplyHealthResponder(
            standing.At,
            standing.Applied.Current,
            standing.Applied.Provisional,
            standing.TunersWereAsked,
            [.. standing.Supplies.Select(supply => new QualitySupplyResponder(
                supply.Silence,
                supply.Watched,
                supply.Quiet,
                Reads(supply, standing.TunersWereAsked)))]);
    }

    private static QualityState Reads(SupplySilenceStanding supply, bool tunersWereAsked)
    {
        if (supply.Quiet > 0)
        {
            return QualityState.Unreachable;
        }

        if (supply.Silence is SupplySilence.SignalSamples && !tunersWereAsked)
        {
            return QualityState.Unreachable;
        }

        return supply.Watched is 0 ? QualityState.NothingToMeasure : QualityState.Good;
    }
}
