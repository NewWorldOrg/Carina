namespace Carina.Conventions.Tests.Fixtures.DomainServices.Domain;

public interface IShelf
{
    int Count { get; }
}

public interface ILedgerDomainService
{
    int Count { get; }
}

public sealed class ShelfDomainService(IShelf shelf, TimeProvider clock)
{
    public (int Count, DateTimeOffset At) Counted => (shelf.Count, clock.GetUtcNow());
}

public sealed class ShelfReader(IShelf shelf)
{
    public int Count => shelf.Count;
}

public sealed class Clocked(TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();
}

public sealed class ShelfFound(IShelf? shelf)
{
    public IShelf? Shelf { get; } = shelf;
}

public sealed class Tally(IReadOnlyList<int> counts)
{
    public int Count => counts.Count;
}

public class UnsealedDomainService(IShelf shelf)
{
    public int Count => shelf.Count;
}

public static class StaticDomainService
{
    public static int Count(IShelf shelf) => shelf?.Count ?? 0;
}

public sealed class NamedDomainService(IShelf shelf, string name)
{
    public (string Name, int Count) Counted => (name, shelf.Count);
}

public sealed class ListedDomainService(IShelf shelf, IReadOnlyList<int> counts)
{
    public int Count => shelf.Count + counts.Count;
}
