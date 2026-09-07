namespace Carina.Domain.Quality;

public sealed record QualityTally
{
    private QualityTally(
        int subjects,
        int good,
        int warning,
        int mayNotBeWatchable,
        int unmeasured,
        int unsupported,
        int unreachable,
        double? average,
        double? lowest,
        double? highest)
    {
        Subjects = subjects;
        Good = good;
        Warning = warning;
        MayNotBeWatchable = mayNotBeWatchable;
        Unmeasured = unmeasured;
        Unsupported = unsupported;
        Unreachable = unreachable;
        Average = average;
        Lowest = lowest;
        Highest = highest;
    }

    public int Subjects { get; }

    public int Good { get; }

    public int Warning { get; }

    public int MayNotBeWatchable { get; }

    public int Unmeasured { get; }

    public int Unsupported { get; }

    public int Unreachable { get; }

    public double? Average { get; }

    public double? Lowest { get; }

    public double? Highest { get; }

    public int Measured => Good + Warning + MayNotBeWatchable;

    public int BeyondThreshold => Warning + MayNotBeWatchable;

    public QualityReading Reading => QualityReading.Of(
        Subjects is 0 || Unsupported < Subjects,
        Unreachable is 0,
        Subjects,
        Measured,
        BeyondThreshold);

    public QualityState State => QualityStates.Of(Reading);

    public double? Worst(ThresholdSense sense)
    {
        ThresholdBand.Named(sense);

        return sense is ThresholdSense.Ceiling ? Highest : Lowest;
    }

    internal static QualityTally Over(IReadOnlyList<QualityObservation> observations)
    {
        int good = 0;
        int warning = 0;
        int mayNotBeWatchable = 0;
        int unmeasured = 0;
        int unsupported = 0;
        int unreachable = 0;
        double total = 0;
        double? lowest = null;
        double? highest = null;

        foreach (QualityObservation observation in observations)
        {
            switch (observation.Standing)
            {
                case QualityStanding.Good:
                    good++;
                    break;
                case QualityStanding.Warning:
                    warning++;
                    break;
                case QualityStanding.MayNotBeWatchable:
                    mayNotBeWatchable++;
                    break;
                case QualityStanding.Unmeasured:
                    unmeasured++;
                    break;
                case QualityStanding.Unsupported:
                    unsupported++;
                    break;
                default:
                    unreachable++;
                    break;
            }

            if (observation.Observed is not { } reading)
            {
                continue;
            }

            total += reading;
            lowest = lowest is { } least ? Math.Min(least, reading) : reading;
            highest = highest is { } most ? Math.Max(most, reading) : reading;
        }

        int measured = good + warning + mayNotBeWatchable;

        return new QualityTally(
            observations.Count,
            good,
            warning,
            mayNotBeWatchable,
            unmeasured,
            unsupported,
            unreachable,
            measured is 0 ? null : total / measured,
            lowest,
            highest);
    }
}

public sealed record QualityGrouping(QualityGroupKey Key, QualityTally Tally);
