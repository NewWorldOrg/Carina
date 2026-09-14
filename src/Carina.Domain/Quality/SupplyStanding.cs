using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed record SupplySilenceStanding(SupplySilence Silence, int Watched, int Quiet);

public sealed record SupplyStanding
{
    private SupplyStanding(
        DateTime at,
        Threshold applied,
        bool tunersWereAsked,
        IReadOnlyList<SupplySilenceStanding> supplies)
    {
        At = at;
        Applied = applied;
        TunersWereAsked = tunersWereAsked;
        Supplies = supplies;
    }

    public DateTime At { get; }

    public Threshold Applied { get; }

    public bool TunersWereAsked { get; }

    public IReadOnlyList<SupplySilenceStanding> Supplies { get; }

    public static SupplyStanding Of(
        DateTime at,
        Threshold applied,
        bool tunersWereAsked,
        IReadOnlyList<SupplySilenceStanding> supplies)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(supplies);

        if (supplies.Select(supply => supply.Silence).Distinct().Count() != supplies.Count)
        {
            throw new ArgumentException("A pass says one thing about each supply it watched.", nameof(supplies));
        }

        return new SupplyStanding(
            UtcTimes.Required(at, nameof(at)),
            applied,
            tunersWereAsked,
            supplies);
    }
}

public interface ISupplyStandingBoard
{
    SupplyStanding? Latest { get; }

    void Held(SupplyStanding standing);
}
