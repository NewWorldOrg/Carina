using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed class ProgrammeSnapshot
{
    public const int SoundsUnannounced = 0;

    public ProgrammeSnapshot(
        string name,
        string summary,
        string extended,
        IReadOnlyList<ProgrammeGenre> genres,
        DateTime capturedAt,
        AudioMode audio,
        int sounds)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(extended);
        ArgumentNullException.ThrowIfNull(genres);
        ArgumentOutOfRangeException.ThrowIfNegative(sounds);

        Name = Within(name, Reservation.NameMaxLength, nameof(name));
        Summary = Within(summary, Reservation.SummaryMaxLength, nameof(summary));
        Extended = Within(extended, Reservation.ExtendedMaxLength, nameof(extended));
        Genres = genres;
        CapturedAt = UtcTimes.Required(capturedAt, nameof(capturedAt));
        Audio = audio;
        Sounds = sounds;
    }

    public string Name { get; }

    public string Summary { get; }

    public string Extended { get; }

    public IReadOnlyList<ProgrammeGenre> Genres { get; }

    public DateTime CapturedAt { get; }

    public AudioMode Audio { get; }

    public int Sounds { get; }

    /// <summary>
    /// What the guide says of <paramref name="programme"/> at <paramref name="at"/>, as a reservation keeps it.
    /// </summary>
    public static ProgrammeSnapshot Of(Programme programme, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return Of(
            programme.Name,
            programme.Summary,
            programme.Items,
            programme.Genres,
            at,
            programme.Audio,
            programme.Sounds);
    }

    public static ProgrammeSnapshot Of(
        string name,
        string summary,
        IReadOnlyList<ProgrammeItem> items,
        IReadOnlyList<ProgrammeGenre> genres,
        DateTime at,
        AudioMode audio,
        int sounds)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(items);

        return new ProgrammeSnapshot(
            Clipped(name, Reservation.NameMaxLength),
            Clipped(summary, Reservation.SummaryMaxLength),
            Clipped(
                string.Join("\n\n", items.Select(item => $"{item.Heading}\n{item.Text}")),
                Reservation.ExtendedMaxLength),
            genres,
            at,
            audio,
            sounds);
    }

    private static string Clipped(string text, int longest)
        => text.Length <= longest ? text : text[..longest];

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
