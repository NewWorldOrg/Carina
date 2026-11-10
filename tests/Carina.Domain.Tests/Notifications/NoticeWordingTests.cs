using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Notifications;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Notifications;

public sealed class NoticeWordingTests
{
    private const string Programme = "港町レシピ帖 #15「灯台守のパン」";

    private const string Station = "みなと総合1";

    private static readonly DateTime Starts = new(2026, 11, 9, 11, 15, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Starts.AddMinutes(45);

    private static readonly Guid RecordingId = Guid.Parse("0f6f2f0e-3b0a-4c58-9d0e-6f1f5f1b7a01");

    private static readonly Guid RowId = Guid.Parse("7a4c0c5e-9a55-4a39-8f0b-2f1d7d3e9b02");

    private static readonly NoticeLinkBase Links = NoticeLinkBase.At(new Uri("https://screens.example"));

    public static TheoryData<RecordingFault> RecordingFaultsNamed => [.. Enum.GetValues<RecordingFault>()];

    public static TheoryData<EncodeFailure> EncodeFailuresNamed => [.. Enum.GetValues<EncodeFailure>()];

    public static TheoryData<RetryGiveUp> GiveUpsNamed => [.. Enum.GetValues<RetryGiveUp>()];

    public static TheoryData<QualityThresholdKey> ThresholdsNamed => [.. Enum.GetValues<QualityThresholdKey>()];

    public static TheoryData<QualitySubjectKind> SubjectsNamed => [.. Enum.GetValues<QualitySubjectKind>()];

    public static TheoryData<TunerTroubleKind> TroublesNamed => [.. Enum.GetValues<TunerTroubleKind>()];

    [Fact(DisplayName = "BR-ND-008: a recording that started is worded with the programme, the station, the time in Japan and when it is due to end")]
    public void ARecordingThatStartedIsWorded()
    {
        NoticeWords words = Worded(Recording(null), NoticeKind.RecordingStarted);

        Assert.Equal("録画を始めました", words.Title);
        Assert.Equal(
            """
            港町レシピ帖 #15「灯台守のパン」
            みなと総合1 · 11/09(月) 20:15〜21:00
            終了予定: 21:00
            """.ReplaceLineEndings("\n"),
            words.Body);
        Assert.Equal("https://screens.example/recordings/0f6f2f0e3b0a4c589d0e6f1f5f1b7a01", words.Link);
    }

    [Fact(DisplayName = "a recording that ended whole is worded with the programme, the station and the time")]
    public void ARecordingThatEndedWholeIsWorded()
    {
        NoticeWords words = Worded(Recording(RecordingOutcome.Complete), NoticeKind.RecordingCompleted);

        Assert.Equal("録画が終わりました", words.Title);
        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15〜21:00", words.Body);
        Assert.Equal("https://screens.example/recordings/0f6f2f0e3b0a4c589d0e6f1f5f1b7a01", words.Link);
    }

    [Fact(DisplayName = "a recording that was cut short is worded with how long was missed and why, in words")]
    public void ARecordingThatWasCutShortIsWorded()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Truncated) with
        {
            MissedMs = 41_200,
            Faults = [RecordingFault.ShortOfTheWindow, RecordingFault.DriverLost, RecordingFault.ShortOfTheWindow],
        };

        NoticeWords words = Worded(recording, NoticeKind.RecordingTruncated);

        Assert.Equal("録画が欠けました", words.Title);
        Assert.Equal(
            """
            港町レシピ帖 #15「灯台守のパン」
            みなと総合1 · 11/09(月) 20:15〜21:00
            欠けた長さ: 42 秒
            理由: 書けた尺が予定に届かなかった、チューナーとの接続が切れた
            """.ReplaceLineEndings("\n"),
            words.Body);
    }

    [Theory(DisplayName = "how long was missed is worded in minutes and seconds, and left out when nothing was")]
    [InlineData(0, null)]
    [InlineData(1, "欠けた長さ: 1 秒")]
    [InlineData(59_000, "欠けた長さ: 59 秒")]
    [InlineData(60_000, "欠けた長さ: 1 分")]
    [InlineData(192_500, "欠けた長さ: 3 分 13 秒")]
    public void HowLongWasMissedIsWorded(long missedMs, string? line)
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Truncated) with { MissedMs = missedMs };

        string[] lines = Worded(recording, NoticeKind.RecordingTruncated).Body.Split('\n');

        Assert.Equal(line, lines.SingleOrDefault(said => said.StartsWith("欠けた長さ", StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "a recording with no reservation that failed is worded with why, in words")]
    public void ARecordingThatFailedIsWorded()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Failed) with
        {
            Reserved = false,
            Faults = [RecordingFault.TuneFailed],
        };

        NoticeWords words = Worded(recording, NoticeKind.RecordingFailed);

        Assert.Equal("録画できませんでした", words.Title);
        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15〜21:00\n理由: 選局失敗", words.Body);
    }

    [Fact(DisplayName = "a station that is not known is left out rather than named by its numbers")]
    public void AStationThatIsNotKnownIsLeftOut()
    {
        NoticeWords words = Worded(Recording(RecordingOutcome.Complete) with { Station = null }, NoticeKind.RecordingCompleted);

        Assert.Equal($"{Programme}\n11/09(月) 20:15〜21:00", words.Body);
    }

    [Fact(DisplayName = "a programme with no name is said to have none")]
    public void AProgrammeWithNoNameIsSaidToHaveNone()
    {
        NoticeWords words = Worded(Recording(RecordingOutcome.Complete) with { ProgrammeName = " " }, NoticeKind.RecordingCompleted);

        Assert.Equal($"(番組名なし)\n{Station} · 11/09(月) 20:15〜21:00", words.Body);
    }

    [Theory(DisplayName = "BR-ND-008: times are worded in Japan's, with the day of the week, whatever the day is in UTC")]
    [InlineData(2026, 11, 9, 12, 0, 54, "11/09(月) 21:00〜21:54")]
    [InlineData(2026, 11, 9, 15, 0, 30, "11/10(火) 00:00〜00:30")]
    [InlineData(2026, 11, 14, 14, 30, 60, "11/14(土) 23:30〜00:30")]
    [InlineData(2026, 12, 31, 14, 59, 2, "12/31(木) 23:59〜00:01")]
    [InlineData(2027, 1, 2, 19, 5, 25, "01/03(日) 04:05〜04:30")]
    public void TimesAreWordedInJapans(int year, int month, int day, int hour, int minute, int minutes, string worded)
    {
        DateTime starts = new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with
        {
            ProgrammeStartsAt = starts,
            EndsAt = starts.AddMinutes(minutes),
        };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.Equal($"{Programme}\n{Station} · {worded}", words.Body);
    }

    [Fact(DisplayName = "BR-ND-008: an end a day or more after the start carries its own day")]
    public void AnEndADayOrMoreAfterTheStartCarriesItsOwnDay()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with { EndsAt = Starts.AddHours(25) };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15〜11/10(火) 21:15", words.Body);
    }

    [Fact(DisplayName = "an end before the start is worded as the start alone")]
    public void AnEndBeforeTheStartIsWordedAsTheStartAlone()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with { EndsAt = Starts.AddMinutes(-1) };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15", words.Body);
    }

    [Fact(DisplayName = "a reservation that lost its tuner to others is worded with the programmes recorded instead")]
    public void AReservationThatLostItsTunerIsWorded()
    {
        ReservationOutcomeNoticeSource outcome = Outcome(ReservationOutcomeKind.Competing) with
        {
            RecordedInstead = ["深夜ドラマ「灯台守の娘」#3", "みなと音楽祭"],
        };

        NoticeWords words = Worded(outcome, NoticeKind.ReservationCompeting);

        Assert.Equal("録画が重なっています", words.Title);
        Assert.Equal(
            """
            港町レシピ帖 #15「灯台守のパン」
            みなと総合1 · 11/09(月) 20:15〜21:00
            代わりに録る番組: 深夜ドラマ「灯台守の娘」#3、みなと音楽祭
            """.ReplaceLineEndings("\n"),
            words.Body);
        Assert.Equal("https://screens.example/reservations", words.Link);
    }

    [Fact(DisplayName = "more programmes recorded instead than three are counted rather than listed")]
    public void ManyProgrammesRecordedInsteadAreCounted()
    {
        ReservationOutcomeNoticeSource outcome = Outcome(ReservationOutcomeKind.Competing) with
        {
            RecordedInstead = ["番組一", "番組二", " ", "番組三", "番組四", "番組五"],
        };

        NoticeWords words = Worded(outcome, NoticeKind.ReservationCompeting);

        Assert.EndsWith("\n代わりに録る番組: 番組一、番組二、番組三 ほか 2 件", words.Body, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-005: a reservation that was not recorded is worded with its classification and why, in words")]
    [InlineData(ReservationOutcomeKind.Missed, "取り逃し")]
    [InlineData(ReservationOutcomeKind.RecordingFailure, "録画の失敗")]
    [InlineData(ReservationOutcomeKind.GaveUpRetrying, "再試行の打ち切り")]
    public void AReservationThatWasNotRecordedIsWorded(ReservationOutcomeKind kind, string classification)
    {
        ReservationOutcomeNoticeSource outcome = Outcome(kind) with
        {
            Faults = [RecordingFault.DiskExhausted],
            GaveUpBecause = RetryGiveUp.BroadcastOver,
        };

        NoticeWords words = Worded(outcome, NoticeKind.ReservationMissed);

        Assert.Equal("録画できませんでした", words.Title);
        Assert.Equal(
            $"{Programme}\n{Station} · 11/09(月) 20:15〜21:00\n分類: {classification}\n理由: 書き込み中にディスクが尽きた、放送が終了",
            words.Body);
        Assert.Equal("https://screens.example/reservations/outcomes", words.Link);
    }

    [Fact(DisplayName = "a reservation that was not recorded for no recorded reason carries its classification alone")]
    public void AReservationMissedForNoRecordedReasonCarriesItsClassificationAlone()
    {
        NoticeWords words = Worded(Outcome(ReservationOutcomeKind.Missed), NoticeKind.ReservationMissed);

        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15〜21:00\n分類: 取り逃し", words.Body);
    }

    [Theory(DisplayName = "a programme that moved or left the guide is worded with the time the reservation now holds")]
    [InlineData(ReservationOutcomeKind.ProgrammeMoved, "番組の時刻が変わりました")]
    [InlineData(ReservationOutcomeKind.ProgrammeGone, "番組が番組表から消えました")]
    public void AProgrammeThatMovedOrLeftIsWorded(ReservationOutcomeKind kind, string title)
    {
        NoticeWords words = Worded(Outcome(kind), NoticeKind.ProgrammeMoved);

        Assert.Equal(title, words.Title);
        Assert.Equal($"{Programme}\n{Station} · 11/09(月) 20:15〜21:00", words.Body);
        Assert.Equal("https://screens.example/reservations/outcomes", words.Link);
    }

    [Theory(DisplayName = "an encode that started or ended is worded with the programme and the format")]
    [InlineData(NoticeKind.EncodeStarted, EncodeCodec.H265, "エンコードを始めました", "形式: H.265", "https://screens.example/settings/encode")]
    [InlineData(NoticeKind.EncodeCompleted, EncodeCodec.H264, "エンコードが終わりました", "形式: H.264", "https://screens.example/recordings/0f6f2f0e3b0a4c589d0e6f1f5f1b7a01")]
    public void AnEncodeIsWorded(NoticeKind kind, EncodeCodec codec, string title, string format, string link)
    {
        NoticeWords words = Worded(EncodeThatEnded() with { Codec = codec }, kind);

        Assert.Equal(title, words.Title);
        Assert.Equal($"{Programme}\n{format}", words.Body);
        Assert.Equal(link, words.Link);
    }

    [Theory(DisplayName = "an encode that ended is worded with how far it shrank from the recording")]
    [InlineData(2_254_857_830, 241_172_480, "大きさ: 2.1 GB → 230 MB(元の 11%)")]
    [InlineData(3_221_225_472, 1_610_612_736, "大きさ: 3.0 GB → 1.5 GB(元の 50%)")]
    [InlineData(1_048_576, 524_288, "大きさ: 1 MB → 512 KB(元の 50%)")]
    [InlineData(1_073_741_824, 1_181_116_006, "大きさ: 1.0 GB → 1.1 GB(元の 110%)")]
    [InlineData(1_073_741_824, 1_048_576, "大きさ: 1.0 GB → 1 MB(元の 1% 未満)")]
    [InlineData(2_048, 100, "大きさ: 2 KB → 1 KB(元の 5%)")]
    public void AnEncodeThatEndedIsWordedWithHowFarItShrank(long sourceBytes, long artefactBytes, string size)
    {
        EncodeNoticeSource ended = EncodeThatEnded() with { SourceBytes = sourceBytes, ArtefactBytes = artefactBytes };

        NoticeWords words = Worded(ended, NoticeKind.EncodeCompleted);

        Assert.Equal($"{Programme}\n形式: H.265\n{size}", words.Body);
    }

    [Theory(DisplayName = "an encode that ended says nothing of its size when either file was not weighed or weighs nothing")]
    [InlineData(null, 241_172_480L)]
    [InlineData(2_254_857_830L, null)]
    [InlineData(0L, 241_172_480L)]
    [InlineData(2_254_857_830L, 0L)]
    public void AnEncodeThatEndedSaysNothingOfASizeItDoesNotKnow(long? sourceBytes, long? artefactBytes)
    {
        EncodeNoticeSource ended = EncodeThatEnded() with { SourceBytes = sourceBytes, ArtefactBytes = artefactBytes };

        NoticeWords words = Worded(ended, NoticeKind.EncodeCompleted);

        Assert.Equal($"{Programme}\n形式: H.265", words.Body);
    }

    [Theory(DisplayName = "an encode that started or failed says nothing of its size")]
    [InlineData(NoticeKind.EncodeStarted)]
    [InlineData(NoticeKind.EncodeFailed)]
    public void AnEncodeThatStartedOrFailedSaysNothingOfItsSize(NoticeKind kind)
    {
        EncodeNoticeSource weighed = Encode(EncodeFailure.TimedOut) with { SourceBytes = 2_254_857_830, ArtefactBytes = 241_172_480 };

        NoticeWords words = Worded(weighed, kind);

        Assert.DoesNotContain("大きさ", words.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "an encode whose profile is gone is worded with the programme alone")]
    public void AnEncodeWhoseProfileIsGoneIsWordedWithTheProgrammeAlone()
    {
        NoticeWords words = Worded(Encode(null) with { Codec = null }, NoticeKind.EncodeStarted);

        Assert.Equal(Programme, words.Body);
    }

    [Fact(DisplayName = "an encode that failed is worded with the classification of the failure")]
    public void AnEncodeThatFailedIsWorded()
    {
        NoticeWords words = Worded(Encode(EncodeFailure.CapabilityUnavailable), NoticeKind.EncodeFailed);

        Assert.Equal("エンコードできませんでした", words.Title);
        Assert.Equal($"{Programme}\n形式: H.265\n理由: 機能不足", words.Body);
        Assert.Equal("https://screens.example/settings/encode", words.Link);
    }

    [Fact(DisplayName = "BR-ND-006: reception falling below a threshold is worded with the measure, what it was measured on, the value and the threshold")]
    public void ReceptionFallingBelowAThresholdIsWorded()
    {
        NoticeWords words = Worded(Quality(QualityIncidentOwner.Quality, null), NoticeKind.QualityIncidentDetected);

        Assert.Equal("受信の品質が下がっています", words.Title);
        Assert.Equal(
            """
            ドロップ率が警告水準を超過
            チャンネル · 11/09(月) 20:15 から
            観測値: 0.8%
            閾値: 0.1%
            """.ReplaceLineEndings("\n"),
            words.Body);
        Assert.Equal("https://screens.example/settings/quality", words.Link);
    }

    [Fact(DisplayName = "BR-ND-006: reception coming back is worded with how long it was down")]
    public void ReceptionComingBackIsWorded()
    {
        QualityNoticeSource quality = Quality(QualityIncidentOwner.Quality, null) with { ResolvedAt = Starts.AddMinutes(10) };

        NoticeWords words = Worded(quality, NoticeKind.QualityIncidentResolved);

        Assert.Equal("受信の品質が戻りました", words.Title);
        Assert.Equal(
            """
            ドロップ率が警告水準を超過
            チャンネル · 11/09(月) 20:15〜20:25
            観測値: 0.8%
            閾値: 0.1%
            """.ReplaceLineEndings("\n"),
            words.Body);
    }

    [Theory(DisplayName = "a measured value is worded in the unit of its measure")]
    [InlineData(QualityThresholdKey.PacketsLostUnwatchable, 0.05, 0.01, "観測値: 5%", "閾値: 1%")]
    [InlineData(QualityThresholdKey.PacketsLeftScrambled, 0.00125, 0.001, "観測値: 0.125%", "閾値: 0.1%")]
    [InlineData(QualityThresholdKey.Overflows, 4, 3, "観測値: 4 回", "閾値: 3 回")]
    [InlineData(QualityThresholdKey.LockRate, 0.912, 0.95, "観測値: 91.2%", "閾値: 95%")]
    [InlineData(QualityThresholdKey.CarrierToNoiseFloor, 17_250, 20_000, "観測値: 17.25 dB", "閾値: 20 dB")]
    [InlineData(QualityThresholdKey.BitErrorRateCeiling, 0.00031, 0.0002, "観測値: 3.1E-4", "閾値: 2.0E-4")]
    [InlineData(QualityThresholdKey.SupplySilence, 900, 600, "観測値: 15 分", "閾値: 10 分")]
    public void AMeasuredValueIsWordedInItsUnit(QualityThresholdKey breached, double observed, double threshold, string observedLine, string thresholdLine)
    {
        QualityNoticeSource quality = Quality(QualityIncidentOwner.Quality, null) with
        {
            Breached = breached,
            Observed = observed,
            Threshold = threshold,
        };

        string[] lines = Worded(quality, NoticeKind.QualityIncidentDetected).Body.Split('\n');

        Assert.Equal([observedLine, thresholdLine], lines[2..]);
    }

    [Fact(DisplayName = "BR-ND-001: a tuner in trouble is worded with what is wrong, and not with the name of the device")]
    public void ATunerInTroubleIsWorded()
    {
        NoticeWords words = Worded(Quality(QualityIncidentOwner.Tuner, TunerTroubleKind.NoLock), NoticeKind.DeviceDisconnected);

        Assert.Equal("チューナーに異常があります", words.Title);
        Assert.Equal("チューナーが電波を掴めない\nチューナー · 11/09(月) 20:15 から", words.Body);
        Assert.Equal("https://screens.example/settings/quality", words.Link);
    }

    [Fact(DisplayName = "a tuner in trouble of a kind that was not kept is worded without it")]
    public void ATunerInTroubleOfNoKeptKindIsWordedWithoutIt()
    {
        NoticeWords words = Worded(Quality(QualityIncidentOwner.Tuner, null), NoticeKind.DeviceDisconnected);

        Assert.Equal("チューナー · 11/09(月) 20:15 から", words.Body);
    }

    [Fact(DisplayName = "D-7: with no address for the screens known, a notice carries no link")]
    public void WithNoAddressKnownANoticeCarriesNoLink()
    {
        foreach (NoticeHappening happening in EveryHappening())
        {
            Assert.Null(NoticeWording.Of(happening, NoticeLinkBase.None).Link);
        }
    }

    [Fact(DisplayName = "BR-ND-001: no notice says the id of the row it was taken from, outside its link")]
    public void NoNoticeSaysTheIdOfItsRow()
    {
        foreach (NoticeHappening happening in EveryHappening())
        {
            NoticeWords words = NoticeWording.Of(happening, Links);
            string said = words.Title + "\n" + words.Body;

            Assert.DoesNotContain(RecordingId.ToString("N")[..8], said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(RowId.ToString("N")[..8], said, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "every kind of notice is worded, and the ledger takes the words")]
    public void EveryKindOfNoticeIsWorded()
    {
        IReadOnlyList<NoticeHappening> happenings = EveryHappening();

        Assert.Equal(Enum.GetValues<NoticeKind>().Order(), happenings.Select(happening => happening.Kind).Distinct().Order());

        foreach (NoticeHappening happening in happenings)
        {
            NoticeWords words = NoticeWording.Of(happening, Links);

            Notice notice = Notice.Take(NoticeId.New(), happening.Kind, happening.SourceId, happening.OccurredAt, words, Ends);

            Assert.Equal(words.Title, notice.Title);
        }
    }

    [Theory(DisplayName = "BR-ND-001: every reason a recording carries is worded, never named as the code names it")]
    [MemberData(nameof(RecordingFaultsNamed))]
    public void EveryReasonARecordingCarriesIsWorded(RecordingFault fault)
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Truncated) with { Faults = [fault] };

        string reason = Worded(recording, NoticeKind.RecordingTruncated).Body.Split('\n')[^1];

        Assert.StartsWith("理由: ", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(fault.ToString(), reason, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-001: every failure of an encode is worded, never named as the code names it")]
    [MemberData(nameof(EncodeFailuresNamed))]
    public void EveryFailureOfAnEncodeIsWorded(EncodeFailure failure)
    {
        string reason = Worded(Encode(failure), NoticeKind.EncodeFailed).Body.Split('\n')[^1];

        Assert.StartsWith("理由: ", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(failure.ToString(), reason, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-001: every reason retrying was given up is worded, never named as the code names it")]
    [MemberData(nameof(GiveUpsNamed))]
    public void EveryReasonRetryingWasGivenUpIsWorded(RetryGiveUp reason)
    {
        ReservationOutcomeNoticeSource outcome = Outcome(ReservationOutcomeKind.GaveUpRetrying) with { GaveUpBecause = reason };

        string said = Worded(outcome, NoticeKind.ReservationMissed).Body.Split('\n')[^1];

        Assert.StartsWith("理由: ", said, StringComparison.Ordinal);
        Assert.DoesNotContain(reason.ToString(), said, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-001: every measure of reception is worded, never named as the code names it")]
    [MemberData(nameof(ThresholdsNamed))]
    public void EveryMeasureOfReceptionIsWorded(QualityThresholdKey breached)
    {
        QualityNoticeSource quality = Quality(QualityIncidentOwner.Quality, null) with { Breached = breached };

        string[] lines = Worded(quality, NoticeKind.QualityIncidentDetected).Body.Split('\n');

        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain(breached.ToString(), lines[0], StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-001: what reception was measured on is worded by its kind alone")]
    [MemberData(nameof(SubjectsNamed))]
    public void WhatReceptionWasMeasuredOnIsWordedByItsKind(QualitySubjectKind subject)
    {
        QualityNoticeSource quality = Quality(QualityIncidentOwner.Quality, null) with { Subject = subject };

        string line = Worded(quality, NoticeKind.QualityIncidentDetected).Body.Split('\n')[1];

        Assert.EndsWith(" · 11/09(月) 20:15 から", line, StringComparison.Ordinal);
        Assert.DoesNotContain(subject.ToString(), line, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-ND-001: every trouble of a tuner is worded, never named as the code names it")]
    [MemberData(nameof(TroublesNamed))]
    public void EveryTroubleOfATunerIsWorded(TunerTroubleKind trouble)
    {
        string[] lines = Worded(Quality(QualityIncidentOwner.Tuner, trouble), NoticeKind.DeviceDisconnected).Body.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(trouble.ToString(), lines[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-002: control characters and bidirectional controls a broadcaster wrote do not reach the words")]
    public void ControlCharactersDoNotReachTheWords()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with
        {
            ProgrammeName = "港町\u202Eレシピ帖\n理由: 偽の行",
            Station = "みなと\u0007総合1\u2067",
        };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.Equal("港町レシピ帖理由: 偽の行\nみなと総合1 · 11/09(月) 20:15〜21:00", words.Body);
    }

    [Fact(DisplayName = "BR-ND-002: markup a broadcaster wrote is kept as written in the ledger, to be escaped for each shape")]
    public void MarkupABroadcasterWroteIsKeptAsWrittenInTheLedger()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with { ProgrammeName = "<!channel> *港町* @everyone" };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.StartsWith("<!channel> *港町* @everyone\n", words.Body, StringComparison.Ordinal);
        Assert.StartsWith(
            "&lt;!channel&gt; ＊港町＊ @everyone\n",
            NoticeMarkup.For(WebhookTarget.Slack, words.Title, words.Body).Body,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ND-003: a programme name longer than the ledger holds is cut by grapheme, and the ledger takes the words")]
    public void AProgrammeNameLongerThanTheLedgerHoldsIsCut()
    {
        RecordingNoticeSource recording = Recording(RecordingOutcome.Complete) with { ProgrammeName = new string('港', 5000) };

        NoticeWords words = Worded(recording, NoticeKind.RecordingCompleted);

        Assert.Equal(Notice.BodyLongest, words.Body.Length);
        Assert.EndsWith("…", words.Body, StringComparison.Ordinal);
        Assert.NotNull(Notice.Take(NoticeId.New(), NoticeKind.RecordingCompleted, RecordingId, Ends, words, Ends));
    }

    [Fact(DisplayName = "a happening is worded as the kind its row can be")]
    public void AHappeningIsWordedAsTheKindItsRowCanBe()
    {
        NoticeHappening happening = new(NoticeKind.EncodeFailed, RecordingId, Starts, Recording(null));

        Assert.Throws<ArgumentException>(() => NoticeWording.Of(happening, Links));
    }

    private static NoticeWords Worded(NoticeSource source, NoticeKind kind)
        => NoticeWording.Of(source.Happenings().Single(happening => happening.Kind == kind), Links);

    private static IReadOnlyList<NoticeHappening> EveryHappening()
    {
        IReadOnlyList<NoticeSource> sources =
        [
            Recording(RecordingOutcome.Complete),
            Recording(RecordingOutcome.Truncated),
            Recording(RecordingOutcome.Failed) with { Reserved = false },
            Outcome(ReservationOutcomeKind.Competing),
            Outcome(ReservationOutcomeKind.Missed),
            Outcome(ReservationOutcomeKind.ProgrammeMoved),
            EncodeThatEnded(),
            Encode(EncodeFailure.TimedOut),
            Quality(QualityIncidentOwner.Quality, null) with { ResolvedAt = Starts.AddMinutes(10) },
            Quality(QualityIncidentOwner.Tuner, TunerTroubleKind.Faulted),
        ];

        return [.. sources.SelectMany(source => source.Happenings())];
    }

    private static RecordingNoticeSource Recording(RecordingOutcome? outcome)
        => new(
            RecordingId,
            Reserved: true,
            Programme,
            Station,
            Starts,
            Ends,
            Starts.AddSeconds(-5),
            outcome is null ? null : Ends,
            outcome,
            MissedMs: 0,
            Faults: []);

    private static ReservationOutcomeNoticeSource Outcome(ReservationOutcomeKind kind)
        => new(
            RowId,
            ReservationId: Guid.NewGuid(),
            kind,
            Programme,
            Station,
            Starts,
            Ends,
            Starts.AddHours(-1),
            Recorded: kind is ReservationOutcomeKind.RecordingFailure ? RecordingOutcome.Failed : null,
            Faults: [],
            GaveUpBecause: null,
            RecordedInstead: []);

    private static EncodeNoticeSource Encode(EncodeFailure? failure)
        => new(
            RowId,
            RecordingId,
            Programme,
            EncodeCodec.H265,
            failure is null ? EncodeJobStatus.Running : EncodeJobStatus.Failed,
            Ends.AddMinutes(1),
            failure is null ? null : Ends.AddMinutes(2),
            failure,
            SourceBytes: null,
            ArtefactBytes: null);

    private static EncodeNoticeSource EncodeThatEnded()
        => Encode(null) with { Status = EncodeJobStatus.Completed, EndedAt = Ends.AddMinutes(2) };

    private static QualityNoticeSource Quality(QualityIncidentOwner owner, TunerTroubleKind? trouble)
        => new(RowId, owner, QualityThresholdKey.PacketsLostWarning, QualitySubjectKind.Channel, 0.008, 0.001, trouble, Starts, ResolvedAt: null);
}
