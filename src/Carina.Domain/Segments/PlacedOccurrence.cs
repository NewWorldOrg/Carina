namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of a catalog entry beside where its programme starts and ends in the recording's own time,
/// when the copy of the programme is known.
/// </summary>
public sealed record PlacedOccurrence(CatalogOccurrence Occurrence, TimeSpan? ProgrammeStarts, TimeSpan? ProgrammeEnds)
{
    public static PlacedOccurrence Of(CatalogOccurrence occurrence, ProgrammeCopy? programme)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new PlacedOccurrence(
            occurrence,
            programme?.ProgrammeStartsAt - programme?.RecordingStartedAt,
            programme?.ProgrammeEndsAt - programme?.RecordingStartedAt);
    }

    /// <summary>
    /// How far into its programme the stretch starts; null when the programme's start is not known.
    /// </summary>
    public TimeSpan? IntoProgramme => Occurrence.StartsAt - ProgrammeStarts;

    /// <summary>
    /// What says which series the stretch's recording is of, with the guide's start of its programme when
    /// that is known.
    /// </summary>
    public SeriesMember Member => Occurrence.Member with { ProgrammeStartsAt = Occurrence.RecordingStartedAt + ProgrammeStarts };

    /// <summary>
    /// The broadcast the stretch is heard in (<see cref="SeriesMember.Episode"/>).
    /// </summary>
    public SeriesEpisode Episode => Member.Episode;
}
