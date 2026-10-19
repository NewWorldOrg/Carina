using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// Recordings made up for learning, ten minutes each, of one weekly series on one station. Every one has a
/// pod of two 15-second adverts between silent cuts at 120 seconds, a silent stretch of 37 seconds that is
/// programme at 250, and 30 seconds between two black frames at 330. Those learned from also have an advert
/// of 75 seconds at 450 that only the catalog hears, heard on another station the week before, and 50
/// seconds of music at 180 that the catalog hears in the middle of an earlier episode and cannot tell the
/// kind of. Their
/// answers make the pod and the advert breaks, and everything else programme.
/// </summary>
internal static class LearningWorld
{
    public const double Length = 600;

    public static readonly CatalogSeries Story = new(1, "架空の物語", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries StoryEarlier = new(1, "架空の物語", new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries Across = new(2, "別の架空の話", new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc));

    public static readonly CatalogSeries Further = new(3, "三つ目の架空の話", new DateTime(2026, 9, 4, 18, 0, 0, DateTimeKind.Utc));

    public static readonly SegmentModelVersion InUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber,
        SegmentModelVersionState.Active,
        false,
        SegmentModel.First,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static readonly SegmentSearch OneSetting = new([SegmentThresholds.Initial], [0.1], [10]);

    public static readonly DateTime Now = Story.Episode(8).RecordingStartedAt;

    public static readonly SegmentSpan[] TaughtAnswer =
    [
        Span(0, 120, SegmentKind.Programme),
        Span(120, 150, SegmentKind.Break),
        Span(150, 450, SegmentKind.Programme),
        Span(450, 525, SegmentKind.Break),
        Span(525, Length, SegmentKind.Programme),
    ];

    public static readonly SegmentSpan[] JudgedAnswer =
    [
        Span(0, 120, SegmentKind.Programme),
        Span(120, 150, SegmentKind.Break),
        Span(150, 330, SegmentKind.Programme),
        Span(330, 360, SegmentKind.Break),
        Span(360, 450, SegmentKind.Programme),
        Span(450, 525, SegmentKind.Break),
        Span(525, Length, SegmentKind.Programme),
    ];

    /// <summary>
    /// <see cref="TaughtAnswer"/> with an opening of 85 seconds at 365, where the catalog hears one in each episode.
    /// </summary>
    public static readonly SegmentSpan[] OpenedAnswer =
    [
        Span(0, 120, SegmentKind.Programme),
        Span(120, 150, SegmentKind.Break),
        Span(150, 365, SegmentKind.Programme),
        Span(365, 450, SegmentKind.Opening),
        Span(450, 525, SegmentKind.Break),
        Span(525, Length, SegmentKind.Programme),
    ];

    /// <summary>
    /// <see cref="JudgedAnswer"/> with the same opening.
    /// </summary>
    public static readonly SegmentSpan[] JudgedOpenedAnswer =
    [
        Span(0, 120, SegmentKind.Programme),
        Span(120, 150, SegmentKind.Break),
        Span(150, 330, SegmentKind.Programme),
        Span(330, 360, SegmentKind.Break),
        Span(360, 365, SegmentKind.Programme),
        Span(365, 450, SegmentKind.Opening),
        Span(450, 525, SegmentKind.Break),
        Span(525, Length, SegmentKind.Programme),
    ];

    /// <summary>
    /// The recordings of <see cref="Taught"/> whose answers also give the opening, as an answer able to name one
    /// would, or say programme there, as every answer kept today does, and the sounds the catalog hears in them,
    /// one opening an episode heard in the same place of the episode two days before.
    /// </summary>
    public static (List<LearningRecording> Recordings, List<CatalogSound> Sounds) Opened(int count, bool answersNameOpenings = true)
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(count);
        List<LearningRecording> recordings =
        [
            .. taught.Select((recording, week) => Recording(recording.Id, week, Readings(null), answersNameOpenings ? OpenedAnswer : TaughtAnswer, true)),
        ];

        sounds.AddRange(recordings.Select((recording, week) => Opening(recording.Id, week)));

        return (recordings, sounds);
    }

    /// <summary>
    /// The recording judged after <see cref="Opened"/>, its answer giving its opening or saying programme there, and
    /// the sound the catalog hears the opening by.
    /// </summary>
    public static (LearningRecording Recording, CatalogSound Sound) JudgedOpened(RecordingId id, int weeks, bool answerNamesOpenings = true)
        => (Recording(id, weeks, Readings(null).Channels(330, 360, 60), answerNamesOpenings ? JudgedOpenedAnswer : JudgedAnswer, true), Opening(id, weeks));

    /// <summary>
    /// A recording of week <paramref name="week"/> that looks like those learned from and is answered with
    /// <paramref name="answer"/>, the answer not confirmed, remembering <paramref name="past"/> of its series.
    /// </summary>
    public static LearningRecording Remembering(RecordingId id, int week, SegmentSpan[] answer, IReadOnlyList<PastEpisode> past)
        => Recording(id, week, Readings(null), answer, false, past: past);

    /// <summary>
    /// The recordings learned from, the first <paramref name="count"/> weeks of the series, and the sounds
    /// the catalog hears in them.
    /// </summary>
    public static (List<LearningRecording> Recordings, List<CatalogSound> Sounds) Taught(int count, bool confirmed = true, byte? quiet = null, bool adverts = true)
    {
        List<LearningRecording> recordings = [];
        List<CatalogSound> sounds = [];

        for (int week = 0; week < count; week++)
        {
            RecordingId id = RecordingId.New();
            CatalogEntryId advert = CatalogEntryId.New();
            CatalogEntryId music = CatalogEntryId.New();

            recordings.Add(Recording(id, week, Readings(quiet), TaughtAnswer, confirmed));
            if (adverts)
            {
                sounds.Add(new CatalogSound(advert, TimeSpan.FromSeconds(75), [Heard(advert, Story, week, 450, 75, id), Heard(advert, Across, week - 1, 300, 75)]));
            }

            sounds.Add(new CatalogSound(music, TimeSpan.FromSeconds(50), [Heard(music, Story, week, 180, 50, id), Heard(music, StoryEarlier, week, CatalogSeries.Lead.TotalSeconds + CatalogSeries.Midpoint, 50)]));
        }

        return (recordings, sounds);
    }

    /// <summary>
    /// The recording judged in the end, the week after <paramref name="weeks"/> weeks were learned from:
    /// its black stretch is a break, the channels unlike inside it, and so is the advert at 450.
    /// </summary>
    public static LearningRecording Judged(
        RecordingId id,
        int weeks,
        IReadOnlyList<ViewerAction>? actions = null,
        IReadOnlyList<PastEpisode>? past = null,
        SegmentSpan[]? answer = null)
        => Recording(id, weeks, Readings(null).Channels(330, 360, 60), answer ?? JudgedAnswer, true, actions, past);

    /// <summary>
    /// A recording of week <paramref name="week"/> that looks like the one judged and has no answer.
    /// </summary>
    public static LearningRecording Unanswered(RecordingId id, int week)
        => Recording(id, week, Readings(null).Channels(330, 360, 60), null, false);

    public static PlacedOccurrence Heard(CatalogEntryId entry, CatalogSeries series, int week, double atRecording, double seconds, RecordingId? recording = null, SegmentKind? answer = null)
        => CatalogSeries.At(entry, series.Episode(week), atRecording - CatalogSeries.Lead.TotalSeconds, seconds, answer, recording);

    /// <summary>
    /// <see cref="InUse"/> as the first version of four kinds would stand in its place.
    /// </summary>
    public static readonly SegmentModelVersion FourKindsInUse = SegmentModelVersion.Rehydrate(
        SegmentModelVersion.FirstNumber + 1,
        SegmentModelVersionState.Active,
        false,
        SegmentModel.FirstOfFourKinds,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        null);

    public static SegmentLearned Learn(
        IReadOnlyList<LearningRecording> recordings,
        IReadOnlyList<CatalogSound> sounds,
        SegmentSearch? search = null,
        SegmentModelVersion? active = null)
        => SegmentLearning.Learn(recordings, new LearningCatalog(sounds, SeriesLinks.None), active ?? InUse, search ?? OneSetting, SegmentSettingsStanding.DefaultSkipThreshold, Now);

    public static string Figures(SegmentMetrics? metrics)
        => metrics is null
            ? "none"
            : $"{metrics.BoundaryRecallWithin2s:F9} {metrics.BoundaryRecallWithin1s:F9} {metrics.ExtraBoundariesPerRecording:F9} {metrics.ProgrammeSkippedSecondsPerHour:F6} "
              + $"{string.Join(",", metrics.Kinds.Select(kind => $"{kind.Kind}:{kind.Recall:F6}:{kind.Precision:F6}"))} {metrics.ReturnedAfterSkipShare:F9} {metrics.Skips}";

    public static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, null);

    private static SyntheticReadings Readings(byte? quiet)
    {
        SyntheticReadings readings = new(Length);

        foreach (double at in new[] { 120.0, 135, 150, 250, 287 })
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

    private static CatalogSound Opening(RecordingId id, int week)
    {
        CatalogEntryId opening = CatalogEntryId.New();

        return new CatalogSound(opening, TimeSpan.FromSeconds(85), [Heard(opening, Story, week, 365, 85, id), Heard(opening, StoryEarlier, week, 365, 85)]);
    }

    private static LearningRecording Recording(
        RecordingId id,
        int week,
        SyntheticReadings readings,
        SegmentSpan[]? answer,
        bool confirmed,
        IReadOnlyList<ViewerAction>? actions = null,
        IReadOnlyList<PastEpisode>? past = null)
    {
        DateTime started = Story.Episode(week).RecordingStartedAt;
        SegmentContext context = new(Story.Episode(week).NetworkId, Story.Episode(week).ServiceId, "架空の物語", null, null, [], [], CornerLogo.None, past ?? []);

        GivenAnswer? given = answer is null
            ? null
            : new GivenAnswer(answer, confirmed, started.AddDays(1), false, null, answer.Any(segment => segment.Kind is SegmentKind.Opening or SegmentKind.Ending));

        return new LearningRecording(id, started, readings.Evidence(), context, given, actions ?? []);
    }
}
