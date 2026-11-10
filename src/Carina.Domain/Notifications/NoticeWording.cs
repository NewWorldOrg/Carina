using System.Globalization;

using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

/// <summary>
/// Words a happening once, in Japanese, for every destination: a title, a body of one fact a line, and a link.
/// </summary>
public static class NoticeWording
{
    private const string ReservationsScreen = "/reservations";

    private const string OutcomesScreen = "/reservations/outcomes";

    private const string EncodeScreen = "/settings/encode";

    private const string QualityScreen = "/settings/quality";

    private const string GuideScreen = "/guide";

    private const string Why = "理由";

    private const string Format = "形式";

    private const string Listed = "、";

    private const int InsteadNamed = 3;

    private static readonly TimeSpan JustBegun = TimeSpan.FromMinutes(1);

    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTemplate<RecordingNoticeSource>> OfRecordings =
        new Dictionary<NoticeKind, NoticeTemplate<RecordingNoticeSource>>
        {
            [NoticeKind.RecordingStarted] = new(
                "録画を始めました",
                recording => About(recording).Fact("終了予定", NoticeTimes.Clock(recording.EndsAt)),
                RecordingScreen),
            [NoticeKind.RecordingCompleted] = new("録画が終わりました", About, RecordingScreen),
            [NoticeKind.RecordingTruncated] = new(
                "録画が欠けました",
                recording => About(recording)
                    .Fact("欠けた長さ", Missed(recording.MissedMs))
                    .Fact(Why, NoticeVocabulary.Of(recording.Faults)),
                RecordingScreen),
            [NoticeKind.RecordingFailed] = new(
                "録画できませんでした",
                recording => About(recording).Fact(Why, NoticeVocabulary.Of(recording.Faults)),
                RecordingScreen),
        };

    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTemplate<ReservationOutcomeNoticeSource>> OfOutcomes =
        new Dictionary<NoticeKind, NoticeTemplate<ReservationOutcomeNoticeSource>>
        {
            [NoticeKind.ReservationCompeting] = new(
                "録画が重なっています",
                outcome => About(outcome).Fact("代わりに録る番組", Instead(outcome.RecordedInstead)),
                _ => ReservationsScreen),
            [NoticeKind.ReservationMissed] = new(
                "録画できませんでした",
                outcome => About(outcome)
                    .Fact("分類", NoticeVocabulary.Miss(outcome.Kind))
                    .Fact(Why, WhyMissed(outcome)),
                _ => OutcomesScreen),
            [NoticeKind.ProgrammeMoved] = new("番組の時刻が変わりました", About, _ => OutcomesScreen),
        };

    private static readonly NoticeTemplate<ReservationOutcomeNoticeSource> OfAProgrammeGone =
        new("番組が番組表から消えました", About, _ => OutcomesScreen);

    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTemplate<EncodeNoticeSource>> OfEncodes =
        new Dictionary<NoticeKind, NoticeTemplate<EncodeNoticeSource>>
        {
            [NoticeKind.EncodeStarted] = new("エンコードを始めました", About, _ => EncodeScreen),
            [NoticeKind.EncodeCompleted] = new(
                "エンコードが終わりました",
                encode => About(encode).Fact("大きさ", NoticeSizes.Shrunk(encode.SourceBytes, encode.ArtefactBytes)),
                encode => RecordingScreen(encode.RecordingId)),
            [NoticeKind.EncodeFailed] = new(
                "エンコードできませんでした",
                encode => About(encode).Fact(Why, NoticeVocabulary.Of(encode.Failure)),
                _ => EncodeScreen),
        };

    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTemplate<QualityNoticeSource>> OfQuality =
        new Dictionary<NoticeKind, NoticeTemplate<QualityNoticeSource>>
        {
            [NoticeKind.QualityIncidentDetected] = new(
                "受信の品質が下がっています",
                quality => Measured(quality, NoticeTimes.Since(quality.DetectedAt)),
                _ => QualityScreen),
            [NoticeKind.QualityIncidentResolved] = new(
                "受信の品質が戻りました",
                quality => Measured(quality, NoticeTimes.Span(quality.DetectedAt, quality.ResolvedAt ?? quality.DetectedAt)),
                _ => QualityScreen),
            [NoticeKind.DeviceDisconnected] = new(
                "チューナーに異常があります",
                quality => new NoticeLines()
                    .Line(NoticeVocabulary.Of(quality.Trouble))
                    .Line("チューナー", NoticeTimes.Since(quality.DetectedAt)),
                _ => QualityScreen),
        };

    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTemplate<ProgrammeWatchNoticeSource>> OfWatches =
        new Dictionary<NoticeKind, NoticeTemplate<ProgrammeWatchNoticeSource>>
        {
            [NoticeKind.ProgrammeStarting] = new(
                "まもなく放送が始まります",
                watched => About(watched).Fact("開始まで", MinutesUntilTheStart(watched)),
                LiveScreen),
            [NoticeKind.WatchedProgrammeMoved] = new("番組の時刻が変わりました", About, LiveScreen),
            [NoticeKind.WatchedProgrammeGone] = new("番組が番組表から消えました", About, _ => GuideScreen),
        };

    private static readonly NoticeTemplate<ProgrammeWatchNoticeSource> OfAProgrammeJustBegun =
        new("放送が始まりました", About, LiveScreen);

    private static readonly NoticeTemplate<ProgrammeWatchNoticeSource> OfAProgrammeOnAir =
        new("放送中です", About, LiveScreen);

    public static NoticeWords Of(NoticeHappening happening, NoticeLinkBase links)
    {
        ArgumentNullException.ThrowIfNull(happening);
        ArgumentNullException.ThrowIfNull(links);

        return happening.Source switch
        {
            RecordingNoticeSource recording => Worded(happening.Kind, recording, OfRecordings, links),
            ReservationOutcomeNoticeSource { Kind: ReservationOutcomeKind.ProgrammeGone } gone
                when happening.Kind is NoticeKind.ProgrammeMoved => Worded(gone, OfAProgrammeGone, links),
            ReservationOutcomeNoticeSource outcome => Worded(happening.Kind, outcome, OfOutcomes, links),
            EncodeNoticeSource encode => Worded(happening.Kind, encode, OfEncodes, links),
            QualityNoticeSource quality => Worded(happening.Kind, quality, OfQuality, links),
            ProgrammeWatchNoticeSource watched
                when happening.Kind is NoticeKind.ProgrammeStarting && watched.ToldAt >= watched.StartsAt
                => Worded(watched, Begun(watched), links),
            ProgrammeWatchNoticeSource watched => Worded(happening.Kind, watched, OfWatches, links),
            _ => throw new ArgumentException("A notice is worded from a row of a ledger it is taken from.", nameof(happening)),
        };
    }

    private static NoticeWords Worded<TSource>(
        NoticeKind kind,
        TSource source,
        IReadOnlyDictionary<NoticeKind, NoticeTemplate<TSource>> templates,
        NoticeLinkBase links)
        where TSource : NoticeSource
    {
        if (!templates.TryGetValue(kind, out NoticeTemplate<TSource>? template))
        {
            throw new ArgumentException($"A row of this ledger is not told of as {kind}.", nameof(kind));
        }

        return Worded(source, template, links);
    }

    private static NoticeWords Worded<TSource>(TSource source, NoticeTemplate<TSource> template, NoticeLinkBase links)
        where TSource : NoticeSource
        => new(
            NoticeText.Cut(template.Title, Notice.TitleLongest),
            NoticeText.Cut(template.Lines(source).Body(), Notice.BodyLongest),
            links.To(template.Screen(source)));

    private static NoticeLines About(RecordingNoticeSource recording)
        => NoticeLines.About(
            recording.ProgrammeName,
            recording.Station,
            NoticeTimes.Span(recording.ProgrammeStartsAt, recording.EndsAt));

    private static NoticeLines About(ReservationOutcomeNoticeSource outcome)
        => NoticeLines.About(outcome.ProgrammeName, outcome.Station, NoticeTimes.Span(outcome.StartsAt, outcome.EndsAt));

    private static NoticeLines About(EncodeNoticeSource encode)
        => NoticeLines.About(encode.ProgrammeName).Fact(Format, NoticeVocabulary.Of(encode.Codec));

    private static NoticeLines About(ProgrammeWatchNoticeSource watched)
        => NoticeLines.About(watched.ProgrammeName, watched.Station, NoticeTimes.Span(watched.StartsAt, watched.EndsAt));

    private static NoticeTemplate<ProgrammeWatchNoticeSource> Begun(ProgrammeWatchNoticeSource watched)
        => watched.ToldAt - watched.StartsAt < JustBegun ? OfAProgrammeJustBegun : OfAProgrammeOnAir;

    private static string? MinutesUntilTheStart(ProgrammeWatchNoticeSource watched)
        => Counted((long)Math.Ceiling((watched.StartsAt - watched.ToldAt).TotalMinutes), "分");

    private static string LiveScreen(ProgrammeWatchNoticeSource watched)
        => string.Create(CultureInfo.InvariantCulture, $"/live?ch={watched.NetworkId}-{watched.ServiceId}");

    private static NoticeLines Measured(QualityNoticeSource quality, string when)
        => new NoticeLines()
            .Line(NoticeVocabulary.Of(quality.Breached))
            .Line(NoticeVocabulary.Of(quality.Subject), when)
            .Fact("観測値", NoticeVocabulary.Measured(quality.Breached, quality.Observed))
            .Fact("閾値", NoticeVocabulary.Measured(quality.Breached, quality.Threshold));

    private static string RecordingScreen(RecordingNoticeSource recording) => RecordingScreen(recording.RecordingId);

    private static string RecordingScreen(Guid recordingId) => $"/recordings/{recordingId:N}";

    private static string? Missed(long missedMs)
    {
        if (missedMs <= 0)
        {
            return null;
        }

        (long minutes, long seconds) = Math.DivRem((missedMs + 999) / 1000, 60);
        IEnumerable<string?> parts = [Counted(minutes, "分"), Counted(seconds, "秒")];

        return string.Join(' ', parts.OfType<string>());
    }

    private static string? Counted(long count, string unit)
        => count > 0 ? string.Create(CultureInfo.InvariantCulture, $"{count} {unit}") : null;

    private static IEnumerable<string> WhyMissed(ReservationOutcomeNoticeSource outcome)
    {
        IEnumerable<string?> gaveUp = [NoticeVocabulary.Of(outcome.GaveUpBecause)];

        return NoticeVocabulary.Of(outcome.Faults).Concat(gaveUp.OfType<string>());
    }

    private static string Instead(IReadOnlyList<string> recordedInstead)
    {
        IReadOnlyList<string> names = [.. recordedInstead.Select(NoticeText.Clean).Where(name => name.Length > 0)];
        string named = string.Join(Listed, names.Take(InsteadNamed));

        return names.Count > InsteadNamed
            ? string.Create(CultureInfo.InvariantCulture, $"{named} ほか {names.Count - InsteadNamed} 件")
            : named;
    }
}
