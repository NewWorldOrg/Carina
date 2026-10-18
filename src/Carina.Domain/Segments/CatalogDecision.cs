namespace Carina.Domain.Segments;

/// <summary>
/// What decided the kind of a catalog entry: an answer given on one of its stretches, or the way its
/// stretches are spread over series, stations and programmes.
/// </summary>
public enum CatalogDecision
{
    Answer = 1,

    Pattern = 2,
}
