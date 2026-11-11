using Carina.Conventions.Tests.Fixtures.DomainServices.Domain;

namespace Carina.Conventions.Tests.Fixtures.DomainServices.Outer;

public sealed class StrayDomainService(IShelf shelf)
{
    public int Count => shelf.Count;
}
