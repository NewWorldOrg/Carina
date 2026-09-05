namespace Carina.Domain.Machines;

/// <summary>
/// Something this machine can be asked to do. What the ffmpeg build was compiled with and what
/// the card will actually accept are separate questions, so each pairing is named on its own.
/// </summary>
public enum Faculty
{
    EncodeH264OnTheProcessor = 1,

    EncodeH265OnTheProcessor = 2,

    EncodeH264OnTheCard = 3,

    EncodeH265OnTheCard = 4,

    DecodeAribCaptions = 5,
}

public static class Faculties
{
    public static readonly IReadOnlyList<Faculty> OnTheCard =
    [
        Faculty.EncodeH264OnTheCard,
        Faculty.EncodeH265OnTheCard,
    ];

    public static Faculty Named(Faculty faculty)
        => Enum.IsDefined(faculty)
            ? faculty
            : throw new ArgumentOutOfRangeException(
                nameof(faculty),
                faculty,
                "A machine is asked about one of the things named here.");

    public static bool NeedsTheCard(Faculty faculty) => OnTheCard.Contains(Named(faculty));
}
