using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed class ProgrammeSnapshot
{
    public ProgrammeSnapshot(
        string name,
        string summary,
        string extended,
        IReadOnlyList<ProgrammeGenre> genres,
        DateTime capturedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(extended);
        ArgumentNullException.ThrowIfNull(genres);

        Name = Within(name, Reservation.NameMaxLength, nameof(name));
        Summary = Within(summary, Reservation.SummaryMaxLength, nameof(summary));
        Extended = Within(extended, Reservation.ExtendedMaxLength, nameof(extended));
        Genres = genres;
        CapturedAt = UtcTimes.Required(capturedAt, nameof(capturedAt));
    }

    public string Name { get; }

    public string Summary { get; }

    public string Extended { get; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; }

    public DateTime CapturedAt { get; }

    private static string Within(string value, int longest, string parameterName)
    {
        if (value.Length > longest)
        {
            throw new ArgumentException(
                $"A snapshot field is at most {longest} characters, but this one has {value.Length}.",
                parameterName);
        }

        return value;
    }
}
