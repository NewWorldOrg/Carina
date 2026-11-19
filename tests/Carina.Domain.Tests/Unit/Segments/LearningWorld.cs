using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Segments;

internal static class LearningWorld
{
    public const double Length = 600;

    public static readonly CatalogSeries Story = new(1, "架空の物語", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries StoryEarlier = new(1, "架空の物語", new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries StoryLongAgo = new(1, "架空の物語", new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries Across = new(2, "別の架空の話", new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc));

    public static readonly SegmentModelVersion InUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber,
        SegmentModelVersionState.Active,
        false,
        HandMadeModels.First,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion WeighingNothingInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber,
        SegmentModelVersionState.Active,
        false,
        new SegmentModel(
            new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Break] = new(0, new Dictionary<string, double>()) }),
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            SegmentThresholds.Initial,
            new Dictionary<string, FeatureScale>()),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion FourKindsInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 1,
        SegmentModelVersionState.Active,
        false,
        HandMadeModels.FirstOfFourKinds,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion BelowPausesInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 2,
        SegmentModelVersionState.Active,
        false,
        HandMadeModels.FourKindsBelowPauses,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion AnySeriesInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 3,
        SegmentModelVersionState.Active,
        false,
        HandMadeModels.FourKindsInAnySeries,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion DynamicSongsInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 4,
        SegmentModelVersionState.Active,
        false,
        HandMadeModels.FourKindsWithDynamicSongs,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentModelVersion PodsInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 4,
        SegmentModelVersionState.Active,
        false,
        SegmentModel.BuiltIn,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentSearch OneSetting = new([SegmentThresholds.Initial], [0.1], [10]);

    public static readonly DateTime Now = Story.Episode(8).RecordingStartedAt;

    private static readonly double[] Stings = [70, 300, 540];

    private const double BreakStarts = 380;

    private static readonly TimeSpan GuideStarts = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan GuideEnds = TimeSpan.FromSeconds(Length - 30);

    public static (List<LearningRecording> Recordings, List<CatalogSound> Sounds) Taught(
        int count,
        bool adverts = false,
        bool openings = false,
        byte? quiet = null,
        bool heardLongAgo = false,
        int fromWeek = 0,
        bool blackAdverts = false,
        bool watched = false,
        int breakAdverts = 0,
        bool stings = true,
        bool asBroadcast = false)
    {
        List<LearningRecording> recordings = [];
        List<CatalogSound> sounds = [];

        for (int week = fromWeek; week < fromWeek + count; week++)
        {
            RecordingId id = RecordingId.New();

            double advert = asBroadcast ? 14.8 : 15;
            double[] breakAt = [.. Enumerable.Range(0, breakAdverts).Select(order => BreakStarts + (advert * order))];
            SyntheticReadings readings = asBroadcast
                ? AsBroadcast(Readings(quiet), [.. breakAt, BreakStarts + (advert * breakAdverts)])
                : Readings(quiet, [.. breakAt, BreakStarts + (advert * breakAdverts)]);

            recordings.Add(Recording(id, week, readings, watched ? Watched(week) : null));
            sounds.AddRange(breakAt.Select(at => Sound(advert, id, week, at, (Across, at + 300), heardLongAgo, otherWeek: week - 1)));
            sounds.Add(Sound(15, id, week, 120, (StoryEarlier, 420), heardLongAgo));
            sounds.Add(Sound(15, id, week, 135, (StoryEarlier, 435), heardLongAgo));
            sounds.AddRange(Stings.Where(_ => stings).Select(at => Sound(20, id, week, at, (StoryEarlier, at), heardLongAgo)));
            sounds.Add(Sound(50, id, week, 180, (StoryEarlier, CatalogSeries.Lead.TotalSeconds + CatalogSeries.Midpoint), heardLongAgo));

            if (adverts)
            {
                sounds.Add(Sound(75, id, week, 450, (Across, 300), heardLongAgo, otherWeek: week - 1));
            }

            if (openings)
            {
                sounds.Add(Sound(85, id, week, 365, (StoryEarlier, 365), heardLongAgo));
            }

            if (blackAdverts)
            {
                sounds.Add(Sound(30, id, week, 330, (StoryEarlier, 700), heardLongAgo));
            }
        }

        return (recordings, sounds);
    }

    public static (LearningRecording Recording, List<CatalogSound> Sounds) Judged(
        RecordingId id,
        int week,
        IReadOnlyList<ViewerAction>? actions = null,
        IReadOnlyList<PastEpisode>? past = null,
        bool opening = false,
        byte? quiet = null)
    {
        List<CatalogSound> sounds =
        [
            Sound(15, id, week, 120, (Across, 200), false, otherWeek: week - 1),
            Sound(15, id, week, 135, (Across, 215), false, otherWeek: week - 1),
            Sound(30, id, week, 330, (Across, 400), false, otherWeek: week - 1),
        ];

        if (opening)
        {
            sounds.Add(Sound(85, id, week, 365, (StoryEarlier, 365), false));
        }

        return (Recording(id, week, Readings(quiet).Channels(330, 360, 60), actions, past), sounds);
    }

    public static LearningRecording Unheard(RecordingId id, int week, IReadOnlyList<PastEpisode>? past = null)
        => Recording(id, week, Readings(null).Channels(330, 360, 60), past: past);

    public static CatalogSound Sound(
        double seconds,
        RecordingId recording,
        int week,
        double atRecording,
        (CatalogSeries Series, double AtRecording) elsewhere,
        bool heardLongAgo,
        int? otherWeek = null)
    {
        CatalogEntryId entry = CatalogEntryId.New();
        List<PlacedOccurrence> stretches =
        [
            Heard(entry, Story, week, atRecording, seconds, recording),
            Heard(entry, elsewhere.Series, otherWeek ?? week, elsewhere.AtRecording, seconds),
        ];

        if (heardLongAgo)
        {
            stretches.Add(Heard(entry, StoryLongAgo, 0, elsewhere.AtRecording, seconds));
        }

        return new CatalogSound(entry, TimeSpan.FromSeconds(seconds), stretches);
    }

    public static PlacedOccurrence Heard(CatalogEntryId entry, CatalogSeries series, int week, double atRecording, double seconds, RecordingId? recording = null)
        => CatalogSeries.At(entry, series.Episode(week), atRecording - CatalogSeries.Lead.TotalSeconds, seconds, recording);

    public static SegmentLearned Learn(
        IReadOnlyList<LearningRecording> recordings,
        IReadOnlyList<CatalogSound> sounds,
        SegmentSearch? search = null,
        SegmentModelVersion? active = null)
        => SegmentLearning.Learn(recordings, new LearningCatalog(sounds, SeriesLinks.None), active ?? InUse, search ?? OneSetting, Now);

    public static string Figures(SegmentMetrics? metrics)
        => metrics is null
            ? "none"
            : $"{metrics.BoundaryRecallWithin2s:F9} {metrics.BoundaryRecallWithin1s:F9} "
              + $"{string.Join(",", metrics.Kinds.Select(kind => $"{kind.Kind}:{kind.Recall:F6}"))} {metrics.ReturnedAfterSkipShare:F9} {metrics.Skips}";

    public static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, null);

    private static SyntheticReadings AsBroadcast(SyntheticReadings readings, IReadOnlyList<double> joins)
    {
        foreach (double join in joins)
        {
            readings.QuietAround(join).Cut(join, 0.2);
        }

        return readings.QuietAround(BreakStarts + 7, 0.3, 102);
    }

    private static SyntheticReadings Readings(byte? quiet, IReadOnlyList<double>? breakCuts = null)
    {
        SyntheticReadings readings = new(Length);

        foreach (double at in new[] { 120.0, 135, 150, 250, 287 }.Concat(breakCuts ?? []))
        {
            if (quiet is byte level)
            {
                readings.Quiet(at - 0.25, at + 0.25, level);
            }
            else
            {
                readings.QuietAround(at);
            }

            readings.Cut(at, 0.9);
        }

        return readings.Black(329.9, 330.1).Black(359.9, 360.1);
    }

    private static ViewerAction[] Watched(int week)
    {
        DateTime after = Story.Episode(week).RecordingStartedAt.AddDays(1);

        return
        [
            new ViewerAction(after, ViewerActionKind.Dismissed, SegmentKind.Break, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(330), 1),
            new ViewerAction(after, ViewerActionKind.Dismissed, SegmentKind.Break, TimeSpan.FromSeconds(360), TimeSpan.FromSeconds(Length), 1),
        ];
    }

    private static LearningRecording Recording(
        RecordingId id,
        int week,
        SyntheticReadings readings,
        IReadOnlyList<ViewerAction>? actions = null,
        IReadOnlyList<PastEpisode>? past = null)
    {
        DateTime started = Story.Episode(week).RecordingStartedAt;
        SegmentContext context = new(Story.Episode(week).NetworkId, Story.Episode(week).ServiceId, "架空の物語", GuideStarts, GuideEnds, [], [], CornerLogo.None, past ?? []);

        return new LearningRecording(id, started, readings.Evidence(), context, actions ?? []);
    }
}
