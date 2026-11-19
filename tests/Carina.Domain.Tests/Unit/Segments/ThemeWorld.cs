using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Segments;

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

    private const byte Softer = 54;

    private static TimeSpan Runs => TimeSpan.FromMinutes(20);

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
