namespace Carina.Domain.Segments;

/// <summary>
/// What an evaluation stands on: how many recordings the catalog is sure of an advert in were judged, how many
/// boundaries of the pods of those adverts they hold, the outer edges of each pod that <see cref="CatalogTruth"/> counts and not the joins inside it, and the latest moment a viewer did something it learned from, when one
/// did; and how many of the recordings judged hold an opening, and an ending, the catalog is sure of. A version is put
/// in use only on <see cref="Enough"/>: at least <see cref="RecordingsNeeded"/> recordings and
/// <see cref="BoundariesNeeded"/> boundaries. The openings, or the endings, are judged on at least
/// <see cref="ThemeRecordingsNeeded"/> recordings holding one (<see cref="Judges"/>): a song heard in one recording
/// alone says how a version does on that one episode.
/// </summary>
public sealed record EvaluationEvidence
{
    public const int RecordingsNeeded = 10;

    public const int BoundariesNeeded = 30;

    public const int ThemeRecordingsNeeded = 2;

    public EvaluationEvidence(int recordings, int boundaries, DateTime? labelsUntil, int openings = 0, int endings = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordings);
        ArgumentOutOfRangeException.ThrowIfNegative(boundaries);
        ArgumentOutOfRangeException.ThrowIfNegative(openings);
        ArgumentOutOfRangeException.ThrowIfNegative(endings);

        Recordings = recordings;
        Boundaries = boundaries;
        LabelsUntil = labelsUntil;
        Openings = openings;
        Endings = endings;
    }

    public int Recordings { get; }

    public int Boundaries { get; }

    public DateTime? LabelsUntil { get; }

    /// <summary>
    /// How many of the recordings judged hold an opening the catalog is sure of.
    /// </summary>
    public int Openings { get; }

    /// <summary>
    /// How many of the recordings judged hold an ending the catalog is sure of.
    /// </summary>
    public int Endings { get; }

    public bool Enough => Recordings >= RecordingsNeeded && Boundaries >= BoundariesNeeded;

    /// <summary>
    /// Whether the openings, or the endings, as <paramref name="kind"/> says, are held in enough recordings to judge a
    /// version on; nothing else is.
    /// </summary>
    public bool Judges(SegmentKind kind) => kind switch
    {
        SegmentKind.Opening => Openings >= ThemeRecordingsNeeded,
        SegmentKind.Ending => Endings >= ThemeRecordingsNeeded,
        _ => false,
    };

    public static EvaluationEvidence Of(IReadOnlyList<SegmentComparison> compared, DateTime? labelsUntil)
    {
        ArgumentNullException.ThrowIfNull(compared);

        return Of([.. compared.Select(comparison => comparison.Truth)], labelsUntil);
    }

    /// <summary>
    /// What the catalog is sure of in some recordings gives an evaluation to stand on: those it is sure of an advert
    /// in, the outer edges of the pods of those adverts, and those it is sure of an opening in and of an ending in.
    /// </summary>
    public static EvaluationEvidence Of(IReadOnlyList<CatalogTruth> truths, DateTime? labelsUntil = null)
    {
        ArgumentNullException.ThrowIfNull(truths);

        CatalogTruth[] breaks = [.. truths.Where(truth => truth.HoldsBreaks)];

        return new EvaluationEvidence(
            breaks.Length,
            breaks.Sum(truth => truth.BreakBoundaries),
            labelsUntil,
            truths.Count(truth => Holds(truth, SegmentKind.Opening)),
            truths.Count(truth => Holds(truth, SegmentKind.Ending)));
    }

    private static bool Holds(CatalogTruth truth, SegmentKind kind) => truth.Stretches.Any(stretch => stretch.Kind == kind);
}
