using System.Linq.Expressions;

using Carina.Domain.Quality;

namespace Carina.Domain.Recordings;

public enum QualityLevel
{
    Good = 1,

    Unmeasured = 2,

    Warning = 3,

    MayNotBeWatchable = 4,
}

/// <summary>
/// One recording read against the levels the quality domain keeps. What was lost and what was left scrambled are
/// judged apart; the scrambling is answered on its own, and the recording as a whole stands at the worse of the two.
/// A counted recording with a gap in it stands at least at the warning level.
/// </summary>
public sealed record RecordingQuality
{
    private RecordingQuality(QualityLevel overall, QualityLevel scrambled)
    {
        Overall = overall;
        Scrambled = scrambled;
    }

    public QualityLevel Overall { get; }

    public QualityLevel Scrambled { get; }

    public static RecordingQuality Of(DropCounters counters, long? scrambledPackets, long missedMs, QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(bands);
        ArgumentOutOfRangeException.ThrowIfNegative(missedMs);

        if (counters.Total is not { } total || counters.Dropped is not { } dropped)
        {
            return new RecordingQuality(QualityLevel.Unmeasured, QualityLevel.Unmeasured);
        }

        if (total is 0)
        {
            return new RecordingQuality(QualityLevel.MayNotBeWatchable, QualityLevel.Unmeasured);
        }

        QualityLevel lost = Read(dropped, total, bands.For(QualityMetric.PacketsLost));
        QualityLevel scrambled = scrambledPackets is { } left
            ? Read(left, total, bands.For(QualityMetric.PacketsLeftScrambled))
            : QualityLevel.Unmeasured;

        QualityLevel worse = lost > scrambled ? lost : scrambled;

        return new RecordingQuality(
            missedMs > 0 && worse < QualityLevel.Warning ? QualityLevel.Warning : worse,
            scrambled);
    }

    /// <summary>
    /// The recordings that lost no packet out of at least one counted, whose packets left scrambled stay under the
    /// warning level and that have no gap in them, as a condition a store can search by.
    /// </summary>
    public static Expression<Func<Recording, bool>> CountedClean(QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        ThresholdBand band = bands.For(QualityMetric.PacketsLeftScrambled);

        if (band.Sense is not ThresholdSense.Ceiling)
        {
            throw new InvalidOperationException("Packets left scrambled are read against a level they stay under.");
        }

        double warning = band.Warning.Current;

        return recording => recording.CcMeasured
            && recording.CcTotalPackets > 0
            && recording.CcDroppedPackets == 0
            && recording.ScrambledPackets != null
            && recording.ScrambledPackets < warning * recording.CcTotalPackets
            && recording.MissedMs == 0;
    }

    private static QualityLevel Read(long counted, long total, ThresholdBand band)
        => ThresholdEvaluator.Judge((double)counted / total, band).Standing switch
        {
            QualityStanding.Good => QualityLevel.Good,
            QualityStanding.Warning => QualityLevel.Warning,
            QualityStanding.MayNotBeWatchable => QualityLevel.MayNotBeWatchable,
            QualityStanding standing => throw new InvalidOperationException(
                $"A share that was counted is judged good, warning or unwatchable, and never {standing}."),
        };
}
