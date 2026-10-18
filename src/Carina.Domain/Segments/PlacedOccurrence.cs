namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of a catalog entry beside where its programme starts and ends in the recording's own time,
/// when the copy of the programme is known, and the kind an answer gives it, when one does.
/// </summary>
public sealed record PlacedOccurrence(CatalogOccurrence Occurrence, TimeSpan? ProgrammeStarts, TimeSpan? ProgrammeEnds, SegmentKind? Answer)
{
    public static PlacedOccurrence Of(CatalogOccurrence occurrence, ProgrammeCopy? programme, SegmentKind? answer)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new PlacedOccurrence(
            occurrence,
            programme?.ProgrammeStartsAt - programme?.RecordingStartedAt,
            programme?.ProgrammeEndsAt - programme?.RecordingStartedAt,
            answer);
    }

    /// <summary>
    /// How far into its programme the stretch starts; null when the programme's start is not known.
    /// </summary>
    public TimeSpan? IntoProgramme => Occurrence.StartsAt - ProgrammeStarts;
}
