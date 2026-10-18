namespace Carina.Domain.Segments;

/// <summary>
/// What a sound the catalog holds is taken for: an advert, a series' opening or ending, a fixed part
/// of a programme (heard as the programme), a fixed part of a station's output between programmes
/// (heard as a break), or not yet known.
/// </summary>
public enum CatalogKind
{
    Cm = 1,

    Opening = 2,

    Ending = 3,

    ProgrammeFixture = 4,

    StationFixture = 5,

    Unknown = 6,
}
