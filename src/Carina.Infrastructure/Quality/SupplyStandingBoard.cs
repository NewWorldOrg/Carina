using Carina.Domain.Quality;

namespace Carina.Infrastructure.Quality;

public sealed class SupplyStandingBoard : ISupplyStandingBoard
{
    private SupplyStanding? latest;

    public SupplyStanding? Latest => Volatile.Read(ref latest);

    public void Held(SupplyStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        Volatile.Write(ref latest, standing);
    }
}
