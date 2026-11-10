using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

/// <summary>
/// The words a notice says a classification in. A classification with no word here is not said.
/// </summary>
internal static class NoticeVocabulary
{
    private static readonly IReadOnlyDictionary<RecordingFault, string> RecordingFaults = new Dictionary<RecordingFault, string>
    {
        [RecordingFault.TuneFailed] = "選局失敗",
        [RecordingFault.RefusedByDiskPrecheck] = "ディスク不足で開始せず",
        [RecordingFault.DiskExhausted] = "書き込み中にディスクが尽きた",
        [RecordingFault.DriverLost] = "チューナーとの接続が切れた",
        [RecordingFault.DrainGraceExpired] = "終了処理が時間切れ",
        [RecordingFault.StoppedByHand] = "手動停止",
        [RecordingFault.TunerContended] = "競合により落とされた",
        [RecordingFault.ScramblingUnresolved] = "スクランブルが解けなかった",
        [RecordingFault.ShortOfTheWindow] = "書けた尺が予定に届かなかった",
        [RecordingFault.NothingLanded] = "0 バイトで終わった",
        [RecordingFault.SizeUnobserved] = "ファイルの大きさを観測できなかった",
        [RecordingFault.StoppedUnasked] = "予期しない停止",
        [RecordingFault.LighterThanTheStream] = "ファイルが尺のわりに小さい",
        [RecordingFault.HeavierThanTheStream] = "ファイルが尺のわりに大きい",
        [RecordingFault.EndStillUndecided] = "終わりが決まらないまま時間切れになった",
        [RecordingFault.LeftRunningUnwatched] = "録画の監視が途切れた",
        [RecordingFault.DriverReplaced] = "チューナーの制御が入れ替わった",
    };

    private static readonly IReadOnlyDictionary<EncodeFailure, string> EncodeFailures = new Dictionary<EncodeFailure, string>
    {
        [EncodeFailure.FfmpegExitedNonZero] = "ffmpeg 非0終了",
        [EncodeFailure.NotEnoughRoom] = "容量不足",
        [EncodeFailure.SourceMissing] = "元ファイル不在",
        [EncodeFailure.CapabilityUnavailable] = "機能不足",
        [EncodeFailure.TimedOut] = "タイムアウト",
        [EncodeFailure.DestinationCollision] = "出力先の衝突",
        [EncodeFailure.HeadTooFar] = "頭出しが遠すぎる",
        [EncodeFailure.EndingNotKept] = "結果を記録できず",
    };

    private static readonly IReadOnlyDictionary<EncodeCodec, string> Codecs = new Dictionary<EncodeCodec, string>
    {
        [EncodeCodec.H264] = "H.264",
        [EncodeCodec.H265] = "H.265",
    };

    private static readonly IReadOnlyDictionary<RetryGiveUp, string> GiveUps = new Dictionary<RetryGiveUp, string>
    {
        [RetryGiveUp.NotTransient] = "一時的な失敗ではない",
        [RetryGiveUp.PrecheckFailed] = "事前の確認で不可",
        [RetryGiveUp.CandidateNeedsAttention] = "選局先に対処が必要",
        [RetryGiveUp.BroadcastOver] = "放送が終了",
        [RetryGiveUp.AttemptsSpent] = "試行の上限",
    };

    private static readonly IReadOnlyDictionary<ReservationOutcomeKind, string> Misses = new Dictionary<ReservationOutcomeKind, string>
    {
        [ReservationOutcomeKind.Missed] = "取り逃し",
        [ReservationOutcomeKind.RecordingFailure] = "録画の失敗",
        [ReservationOutcomeKind.GaveUpRetrying] = "再試行の打ち切り",
    };

    private static readonly IReadOnlyDictionary<QualityThresholdKey, string> Breaches = new Dictionary<QualityThresholdKey, string>
    {
        [QualityThresholdKey.PacketsLostWarning] = "ドロップ率が警告水準を超過",
        [QualityThresholdKey.PacketsLostUnwatchable] = "ドロップ率が視聴不可の恐れを超過",
        [QualityThresholdKey.PacketsLeftScrambled] = "スクランブル残存率が上限を超過",
        [QualityThresholdKey.PacketsLeftScrambledUnwatchable] = "スクランブル残存率が視聴不可の恐れを超過",
        [QualityThresholdKey.Overflows] = "取りこぼしが上限を超過",
        [QualityThresholdKey.LockRate] = "lock 率が下限を下回った",
        [QualityThresholdKey.CarrierToNoiseFloor] = "CNR が下限を下回った",
        [QualityThresholdKey.BitErrorRateCeiling] = "post-Viterbi ビット誤り率が上限を超過",
        [QualityThresholdKey.SupplySilence] = "供給が途絶",
    };

    private static readonly IReadOnlyDictionary<QualityThresholdKey, NoticeUnit> Units = new Dictionary<QualityThresholdKey, NoticeUnit>
    {
        [QualityThresholdKey.PacketsLostWarning] = NoticeUnit.Share,
        [QualityThresholdKey.PacketsLostUnwatchable] = NoticeUnit.Share,
        [QualityThresholdKey.PacketsLeftScrambled] = NoticeUnit.Share,
        [QualityThresholdKey.PacketsLeftScrambledUnwatchable] = NoticeUnit.Share,
        [QualityThresholdKey.Overflows] = NoticeUnit.Count,
        [QualityThresholdKey.LockRate] = NoticeUnit.Share,
        [QualityThresholdKey.CarrierToNoiseFloor] = NoticeUnit.Decibels,
        [QualityThresholdKey.BitErrorRateCeiling] = NoticeUnit.ErrorRate,
        [QualityThresholdKey.SupplySilence] = NoticeUnit.Minutes,
    };

    private static readonly IReadOnlyDictionary<QualitySubjectKind, string> Subjects = new Dictionary<QualitySubjectKind, string>
    {
        [QualitySubjectKind.Tuner] = "チューナー",
        [QualitySubjectKind.Channel] = "チャンネル",
        [QualitySubjectKind.Recording] = "録画",
        [QualitySubjectKind.TransportStream] = "TS",
        [QualitySubjectKind.Guide] = "番組表",
        [QualitySubjectKind.Reception] = "受信",
    };

    private static readonly IReadOnlyDictionary<TunerTroubleKind, string> Troubles = new Dictionary<TunerTroubleKind, string>
    {
        [TunerTroubleKind.NoLock] = "チューナーが電波を掴めない",
        [TunerTroubleKind.RepeatedTuneFailure] = "チューナーの割り当てが止まっている",
        [TunerTroubleKind.LedgerDisagrees] = "チューナーの割り当てが止まっている",
        [TunerTroubleKind.DeviceFailed] = "チューナーの割り当てが止まっている",
        [TunerTroubleKind.DeviceFailedAgain] = "チューナーの割り当てが止まっている",
        [TunerTroubleKind.Faulted] = "チューナーの割り当てが止まっている",
        [TunerTroubleKind.TuneFailing] = "チューナーの選局が失敗している",
        [TunerTroubleKind.Degraded] = "チューナーの健全性が下がっている",
    };

    public static IEnumerable<string> Of(IEnumerable<RecordingFault> faults) => Said(faults, RecordingFaults);

    public static string? Of(EncodeFailure? failure) => Said(failure, EncodeFailures);

    public static string? Of(EncodeCodec? codec) => Said(codec, Codecs);

    public static string? Of(RetryGiveUp? reason) => Said(reason, GiveUps);

    public static string? Miss(ReservationOutcomeKind kind) => Said(kind, Misses);

    public static string? Of(QualityThresholdKey breached) => Said(breached, Breaches);

    public static string? Of(QualitySubjectKind subject) => Said(subject, Subjects);

    public static string? Of(TunerTroubleKind? trouble) => Said(trouble, Troubles);

    public static string? Measured(QualityThresholdKey breached, double value)
        => Units.TryGetValue(breached, out NoticeUnit unit) ? NoticeUnits.Said(unit, value) : null;

    private static IEnumerable<string> Said<T>(IEnumerable<T> classified, IReadOnlyDictionary<T, string> words)
        where T : struct, Enum
        => classified.Select(one => words.GetValueOrDefault(one)).OfType<string>();

    private static string? Said<T>(T? classified, IReadOnlyDictionary<T, string> words)
        where T : struct, Enum
        => classified is { } one ? words.GetValueOrDefault(one) : null;
}
