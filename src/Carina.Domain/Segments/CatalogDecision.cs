namespace Carina.Domain.Segments;

/// <summary>
/// What decided the kind of a catalog entry: the way its stretches are spread over series, stations and
/// programmes, or the opening or ending whose pieces its stretches carry on (<see cref="CatalogKinds.CarryOn"/>).
/// </summary>
public enum CatalogDecision
{
    Pattern = 2,

    Continuation = 3,
}
