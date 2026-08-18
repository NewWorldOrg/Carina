using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public sealed class CollectionEpoch
{
    public const int TheOnlyRow = 1;

    private CollectionEpoch()
    {
    }

    public int Id { get; private set; }

    public int Generation { get; private set; }

    public DateTime AdvancedAt { get; private set; }

    public static CollectionEpoch Begin(DateTime at) => Rehydrate(TheOnlyRow, 1, at);

    public static CollectionEpoch Rehydrate(int id, int generation, DateTime advancedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);

        return new CollectionEpoch
        {
            Id = id,
            Generation = generation,
            AdvancedAt = UtcTimes.Required(advancedAt, nameof(advancedAt)),
        };
    }

    public void Advance(DateTime at)
    {
        Generation++;
        AdvancedAt = UtcTimes.Required(at, nameof(at));
    }
}

public interface ICollectionEpochRepository
{
    Task<CollectionEpoch> ReadAsync(DateTime at, CancellationToken cancellationToken);

    Task SaveAsync(CollectionEpoch epoch, CancellationToken cancellationToken);
}
