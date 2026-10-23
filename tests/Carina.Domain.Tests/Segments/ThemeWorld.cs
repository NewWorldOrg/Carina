using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// Recordings made up for learning the opening and the ending, of three weekly series on three stations, each
/// recording starting a minute before a programme of twenty minutes and ending half a minute after it. Every one
/// has a song of 88 seconds, or as long as asked, for its opening at the programme's start, a pod of two 15-second
/// adverts right after it and another at 700 seconds, an insert song of 80 seconds at 480 in the middle, and a song as
/// long as its opening for its ending at 1110, whose loudness moves when asked; speech fills the rest. Every edge is a silent cut. The catalog
/// hears each advert there and in another series the week before, so it is sure of it; it hears the opening and the
/// ending of a series as such once two of its episodes hold them, and hears nothing of a series with one episode.
/// </summary>
internal static class ThemeWorld
{
    public const double Length = 1290;

    public const double OpeningStarts = 60;

    public const double EndingStarts = 1110;

    public const double ThemeLasts = 88;

    public static readonly CatalogSeries Mystery = new(3, "架空の推理", new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc), Runs);

    public static readonly CatalogSeries Voyage = new(4, "架空の航海", new DateTime(2026, 9, 3, 13, 0, 0, DateTimeKind.Utc), Runs);

    public static readonly CatalogSeries Garden = new(5, "架空の庭", new DateTime(2026, 9, 4, 14, 0, 0, DateTimeKind.Utc), Runs);

    public static readonly DateTime Now = Garden.Episode(4).RecordingStartedAt;

    public static readonly SegmentSearch OneSetting = new([HandMadeModels.FourKindsInAnySeries.Thresholds], [0.1], [10]);

    private static readonly CatalogSeries Elsewhere = new(6, "架空の番組", new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc));

    private const double InsertStarts = 480;

    /// <summary>
    /// The level an ending whose loudness moves drops to for a second and a fifth every four seconds, at -27 dBFS, which
    /// gives it a loudness range of 7 decibels.
    /// </summary>
    private const byte Softer = 54;

    private static TimeSpan Runs => TimeSpan.FromMinutes(20);

    /// <summary>
    /// The first <paramref name="weeks"/> episodes of <paramref name="series"/>, and the sounds the catalog hears in
    /// them: their adverts, and their opening and ending once there are two episodes.
    /// </summary>
    public static (List<LearningRecording> Recordings, List<CatalogSound> Sounds) Episodes(CatalogSeries series, int weeks, double lasts = ThemeLasts, bool movingEnding = false)
    {
        List<LearningRecording> recordings = [];
        List<CatalogSound> sounds = [];
        List<(RecordingId Id, int Week)> aired = [];

        for (int week = 0; week < weeks; week++)
        {
            RecordingId id = RecordingId.New();

            recordings.Add(Recording(id, series, week, lasts, movingEnding));
            sounds.AddRange(new[] { OpeningStarts + lasts, OpeningStarts + lasts + 15, 700, 715 }.Select(at => Advert(series, week, id, at)));
            aired.Add((id, week));
        }

        if (weeks >= 2)
        {
            sounds.Add(Theme(series, aired, OpeningStarts, lasts));
            sounds.Add(Theme(series, aired, EndingStarts, lasts));
        }

        return (recordings, sounds);
    }

    public static SegmentLearned Learn(IReadOnlyList<LearningRecording> recordings, IReadOnlyList<CatalogSound> sounds, SegmentModel inUse)
        => SegmentLearning.Learn(
            recordings,
            new LearningCatalog(sounds, SeriesLinks.None),
            SegmentModelVersion.Rehydrate(SegmentModelVersion.FirstNumber + 3, SegmentModelVersionState.Active, false, inUse, Mystery.Episode(0).RecordingStartedAt, Mystery.Episode(0).RecordingStartedAt, null, null, null),
            OneSetting,
            Now);

    /// <summary>
    /// The recording detected by <paramref name="model"/> hearing nothing of the catalog and remembering no earlier
    /// episode, as the first episode of a series is.
    /// </summary>
    public static SegmentDetection Unheard(LearningRecording recording, SegmentModel model)
        => SegmentDetector.Detect(recording.Evidence, recording.Context.Hearing([]).Remembering([]), model);

    public static (double Starts, double Ends, SegmentKind Kind)[] Themes(SegmentDetection detection)
        => [.. detection.Segments.Where(span => span.Kind is SegmentKind.Opening or SegmentKind.Ending).Select(span => (span.Starts.TotalSeconds, span.Ends.TotalSeconds, span.Kind))];

    private static LearningRecording Recording(RecordingId id, CatalogSeries series, int week, double lasts, bool movingEnding)
    {
        ProgrammeCopy episode = series.Episode(week);
        SegmentContext context = new(
            episode.NetworkId,
            episode.ServiceId,
            episode.SeriesName,
            CatalogSeries.Lead,
            CatalogSeries.Lead + Runs,
            [],
            [],
            CornerLogo.None,
            []);

        return new LearningRecording(id, episode.RecordingStartedAt, Readings(lasts, movingEnding).Evidence(), context, []);
    }

    private static SyntheticReadings Readings(double lasts, bool movingEnding)
    {
        SyntheticReadings readings = new SyntheticReadings(Length)
            .Spoken(0, OpeningStarts)
            .Spoken(OpeningStarts + lasts + 30, InsertStarts)
            .Spoken(InsertStarts + 80, 700)
            .Spoken(730, EndingStarts)
            .Spoken(EndingStarts + lasts, Length);
        double[] cuts =
        [
            OpeningStarts,
            OpeningStarts + lasts,
            OpeningStarts + lasts + 15,
            OpeningStarts + lasts + 30,
            InsertStarts,
            InsertStarts + 80,
            700,
            715,
            730,
            EndingStarts,
            EndingStarts + lasts,
        ];

        foreach (double cut in cuts)
        {
            readings.QuietAround(cut).Cut(cut, 0.9);
        }

        if (movingEnding)
        {
            for (double beat = EndingStarts + 2; beat + 1.2 < EndingStarts + lasts - 1; beat += 4)
            {
                readings.Quiet(beat, beat + 1.2, Softer);
            }
        }

        return readings;
    }

    private static CatalogSound Advert(CatalogSeries series, int week, RecordingId recording, double atRecording)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new CatalogSound(
            entry,
            TimeSpan.FromSeconds(15),
            [
                LearningWorld.Heard(entry, series, week, atRecording, 15, recording),
                LearningWorld.Heard(entry, Elsewhere, week, 300, 15),
            ]);
    }

    private static CatalogSound Theme(CatalogSeries series, IReadOnlyList<(RecordingId Id, int Week)> aired, double atRecording, double lasts)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new CatalogSound(
            entry,
            TimeSpan.FromSeconds(lasts),
            [.. aired.Select(episode => LearningWorld.Heard(entry, series, episode.Week, atRecording, lasts, episode.Id))]);
    }
}
