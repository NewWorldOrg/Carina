using Carina.Contracts;

namespace Carina.Domain.Quality;

/// <summary>
/// One finished session that counted its packets, with what its locked samples usually read.
/// </summary>
public sealed record SessionSignal(
    string DriverInstanceId,
    DateTime StartedAt,
    DateTime EndedAt,
    long DroppedPackets,
    long TotalPackets,
    long Overflows,
    double? CarrierToNoise,
    double? BitErrorRate);

public static class SignalThresholdMeasure
{
    public const int FewestOnEachSide = 10;

    private const int CarrierToNoiseStep = 100;

    private const int SignificantFigures = 2;

    public static readonly IReadOnlyList<QualityThresholdKey> Keys =
    [
        QualityThresholdKey.CarrierToNoiseFloor,
        QualityThresholdKey.BitErrorRateCeiling,
    ];

    public static readonly TimeSpan AroundADriverStart = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Reads each finished session that counted its packets at what its locked samples usually read.
    /// </summary>
    public static IReadOnlyList<SessionSignal> Read(
        IReadOnlyList<QualitySessionMeasurement> sessions,
        IReadOnlyList<QualitySignalSample> samples)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(samples);

        ILookup<(string Instance, SessionId Session), QualitySignalSample> bySession = samples
            .Where(sample => sample.Signal.Locked)
            .ToLookup(sample => (sample.DriverInstanceId, sample.Session));

        return
        [
            .. sessions
                .Where(session => session is { HasEnded: true, CcMeasured: true })
                .OrderBy(session => session.StartedAt)
                .Select(session => Read(session, [.. bySession[(session.DriverInstanceId, session.Session)]])),
        ];
    }

    /// <summary>
    /// Leaves out the sessions whose drops say nothing of the signal: those the driver overflowed in, those that
    /// counted no packet, those begun just after a driver started and those that ran into the end of a driver
    /// another one replaced.
    /// </summary>
    public static IReadOnlyList<SessionSignal> Considered(IReadOnlyList<SessionSignal> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        Dictionary<string, DateTime> started = sessions
            .GroupBy(session => session.DriverInstanceId, StringComparer.Ordinal)
            .ToDictionary(driver => driver.Key, driver => driver.Min(session => session.StartedAt), StringComparer.Ordinal);
        DateTime latestStart = started.Count is 0 ? DateTime.MinValue : started.Values.Max();
        Dictionary<string, DateTime> replacedAt = sessions
            .Where(session => started[session.DriverInstanceId] < latestStart)
            .GroupBy(session => session.DriverInstanceId, StringComparer.Ordinal)
            .ToDictionary(driver => driver.Key, driver => driver.Max(session => session.EndedAt), StringComparer.Ordinal);

        return
        [
            .. sessions.Where(session => session.Overflows is 0
                                         && session.TotalPackets > 0
                                         && session.StartedAt >= started[session.DriverInstanceId] + AroundADriverStart
                                         && !RanIntoAReplacement(session, replacedAt)),
        ];
    }

    /// <summary>
    /// The level that best tells the sessions that dropped from the ones that did not, or null when there are too
    /// few of either or the level leaves more than a fifth of either side on the wrong side.
    /// </summary>
    public static QualityThresholdMeasurement? Measure(
        QualityThresholdKey key,
        IReadOnlyList<SessionSignal> sessions,
        double droppedFrom,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (!Keys.Contains(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Only the two signal levels are measured.");
        }

        QualityThresholdShape shape = QualityThresholdShapes.Of(key);
        IReadOnlyList<SessionSignal> counted = [.. Considered(sessions).Where(session => Reading(key, session) is not null)];
        IReadOnlyList<Judged> judged =
        [
            .. counted.Select(session => new Judged(
                Reading(key, session)!.Value,
                (double)session.DroppedPackets / session.TotalPackets >= droppedFrom)),
        ];

        if (Split(judged, shape.Sense) is not { } split)
        {
            return null;
        }

        double value = Rounded(key, split);

        if (!shape.Holds(value) || !Separates(judged, value, shape.Sense))
        {
            return null;
        }

        return QualityThresholdMeasurement.Of(
            value,
            judged.Count,
            judged.Count(session => session.Dropped),
            counted.Min(session => session.StartedAt),
            counted.Max(session => session.EndedAt),
            at);
    }

    private static SessionSignal Read(QualitySessionMeasurement session, IReadOnlyList<QualitySignalSample> samples)
        => new(
            session.DriverInstanceId,
            session.StartedAt,
            session.EndedAt!.Value,
            session.CcDroppedPackets!.Value,
            session.CcTotalPackets!.Value,
            session.EovfCount,
            UsualReadings.Of(samples
                .Where(sample => sample.Signal.CarrierToNoiseMilliDecibels is not null)
                .Select(sample => new WeighedReading(sample.Signal.CarrierToNoiseMilliDecibels!.Value, 1))
                .OrderBy(weighed => weighed.Reading)),
            UsualReadings.Of(samples
                .Select(WorstLayer)
                .OfType<double>()
                .Select(rate => new WeighedReading(rate, 1))
                .OrderByDescending(weighed => weighed.Reading)));

    private static double? WorstLayer(QualitySignalSample sample)
    {
        double[] rates = [.. sample.Signal.BitErrors.Select(layer => layer.ErrorRate).OfType<double>()];

        return rates.Length is 0 ? null : rates.Max();
    }

    private static bool RanIntoAReplacement(SessionSignal session, IReadOnlyDictionary<string, DateTime> replacedAt)
        => replacedAt.TryGetValue(session.DriverInstanceId, out DateTime last)
           && session.EndedAt > last - AroundADriverStart;

    private static double? Reading(QualityThresholdKey key, SessionSignal session) => key switch
    {
        QualityThresholdKey.CarrierToNoiseFloor => session.CarrierToNoise,
        QualityThresholdKey.BitErrorRateCeiling => session.BitErrorRate,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Only the two signal levels are measured."),
    };

    private static double? Split(IReadOnlyList<Judged> judged, ThresholdSense sense)
    {
        double[] values = [.. judged.Select(session => session.Value).Distinct().Order()];
        Candidate? best = null;

        for (int below = 0; below + 1 < values.Length; below++)
        {
            double between = (values[below] + values[below + 1]) / 2;
            Candidate candidate = new(between, values[below + 1] - values[below], Misjudged(judged, values[below], values[below + 1], sense));

            if (best is null || candidate.Beats(best))
            {
                best = candidate;
            }
        }

        return best?.Value;
    }

    private static int Misjudged(IReadOnlyList<Judged> judged, double below, double above, ThresholdSense sense)
        => judged.Count(session => session.Dropped != DropSide(session.Value, below, above, sense));

    private static bool DropSide(double value, double below, double above, ThresholdSense sense)
        => sense is ThresholdSense.Floor ? value <= below : value >= above;

    private static bool Separates(IReadOnlyList<Judged> judged, double level, ThresholdSense sense)
    {
        int dropped = judged.Count(session => session.Dropped);
        int kept = judged.Count - dropped;
        int caught = judged.Count(session => session.Dropped && Reaches(session.Value, level, sense));
        int spared = judged.Count(session => !session.Dropped && !Reaches(session.Value, level, sense));

        return dropped >= FewestOnEachSide
               && kept >= FewestOnEachSide
               && caught * 5 >= dropped * 4
               && spared * 5 >= kept * 4;
    }

    private static bool Reaches(double value, double level, ThresholdSense sense)
        => sense is ThresholdSense.Ceiling ? value >= level : value <= level;

    private static double Rounded(QualityThresholdKey key, double value) => key switch
    {
        QualityThresholdKey.CarrierToNoiseFloor =>
            Math.Round(value / CarrierToNoiseStep, MidpointRounding.AwayFromZero) * CarrierToNoiseStep,
        _ => ToSignificantFigures(value),
    };

    private static double ToSignificantFigures(double value)
    {
        if (value <= 0)
        {
            return value;
        }

        double scale = Math.Pow(10, SignificantFigures - 1 - (int)Math.Floor(Math.Log10(value)));

        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private sealed record Judged(double Value, bool Dropped);

    private sealed record Candidate(double Value, double Gap, int Misjudged)
    {
        public bool Beats(Candidate other)
            => Misjudged < other.Misjudged || (Misjudged == other.Misjudged && Gap > other.Gap);
    }
}
