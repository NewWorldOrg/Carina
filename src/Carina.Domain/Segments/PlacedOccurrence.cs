namespace Carina.Domain.Segments;

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

    public TimeSpan? IntoProgramme => Occurrence.StartsAt - ProgrammeStarts;

    public SeriesMember Member => Occurrence.Member with { ProgrammeStartsAt = Occurrence.RecordingStartedAt + ProgrammeStarts };

    public SeriesEpisode Episode => Member.Episode;
}
