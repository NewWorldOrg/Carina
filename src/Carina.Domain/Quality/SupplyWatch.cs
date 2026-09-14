using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public enum SupplySilence
{
    RecordingProgress = 1,

    RecordingMeasurement = 2,

    SignalSamples = 3,

    GuideVisits = 4,
}

public static class SupplySilences
{
    public static IReadOnlySet<SupplySilence> Every { get; } = Enum.GetValues<SupplySilence>().ToHashSet();

    public static IReadOnlySet<SupplySilence> TheDriverAnswersFor { get; } =
        new HashSet<SupplySilence> { SupplySilence.SignalSamples };

    public static IReadOnlySet<SupplySilence> TheLedgerAnswersFor { get; } =
        Every.Where(silence => !TheDriverAnswersFor.Contains(silence)).ToHashSet();
}

public enum SupplyWatchStep
{
    Nothing = 1,

    Open = 2,

    Resolve = 3,
}

public sealed record SupplyReading
{
    private SupplyReading(SupplySilence silence, QualitySubject subject, DateTime lastHeardAt)
    {
        Silence = silence;
        Subject = subject;
        LastHeardAt = lastHeardAt;
    }

    public SupplySilence Silence { get; }

    public QualitySubject Subject { get; }

    public DateTime LastHeardAt { get; }

    public static SupplyReading Of(SupplySilence silence, QualitySubject subject, DateTime lastHeardAt)
    {
        if (!Enum.IsDefined(silence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(silence),
                silence,
                "A supply going quiet is one of the four this domain watches.");
        }

        ArgumentNullException.ThrowIfNull(subject);

        return new SupplyReading(silence, subject, UtcTimes.Required(lastHeardAt, nameof(lastHeardAt)));
    }
}

public sealed record SupplySilenceFinding
{
    private SupplySilenceFinding(SupplySilence silence, QualitySubject subject, TimeSpan quiet)
    {
        Silence = silence;
        Subject = subject;
        Quiet = quiet;
    }

    public SupplySilence Silence { get; }

    public QualitySubject Subject { get; }

    public TimeSpan Quiet { get; }

    public double Seconds => Quiet.TotalSeconds;

    public static SupplySilenceFinding Of(SupplySilence silence, QualitySubject subject, TimeSpan quiet)
    {
        if (!Enum.IsDefined(silence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(silence),
                silence,
                "A supply going quiet is one of the four this domain watches.");
        }

        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfNegative(quiet.Ticks, nameof(quiet));

        return new SupplySilenceFinding(silence, subject, quiet);
    }
}

public sealed record SupplyWatchPlan(
    IReadOnlyList<SupplySilenceFinding> ToOpen,
    IReadOnlyList<QualityIncident> ToResolve);

public static class SupplyWatch
{
    public static SupplyWatchStep NextStep(bool observed, bool quiet, bool standing)
        => (observed, quiet, standing) switch
        {
            (false, _, _) => SupplyWatchStep.Nothing,
            (true, true, false) => SupplyWatchStep.Open,
            (true, false, true) => SupplyWatchStep.Resolve,
            _ => SupplyWatchStep.Nothing,
        };

    public static IReadOnlyList<SupplySilenceFinding> Quiet(
        IReadOnlyList<SupplyReading> readings,
        TimeSpan longest,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(readings);
        UtcTimes.Required(now, nameof(now));

        if (longest <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longest),
                longest,
                "A supply is only quiet for longer than nothing.");
        }

        List<SupplySilenceFinding> found = [];

        foreach (SupplyReading reading in readings)
        {
            TimeSpan silent = now - reading.LastHeardAt;

            if (silent >= longest)
            {
                found.Add(SupplySilenceFinding.Of(reading.Silence, reading.Subject, silent));
            }
        }

        return found;
    }

    public static SupplyWatchPlan Plan(
        IReadOnlyList<SupplySilenceFinding> quiet,
        IReadOnlyList<QualityIncident> standing,
        IReadOnlySet<SupplySilence> observed)
    {
        ArgumentNullException.ThrowIfNull(quiet);
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(observed);

        List<QualityIncident> watched = [.. standing.Where(Watched)];

        List<SupplySilenceFinding> opening =
        [
            .. quiet.Where(finding => NextStep(
                observed.Contains(finding.Silence),
                true,
                watched.Exists(incident => About(incident, finding))) is SupplyWatchStep.Open),
        ];

        List<QualityIncident> resolving =
        [
            .. watched.Where(incident => NextStep(
                incident.Silence is { } silence && observed.Contains(silence),
                quiet.Any(finding => About(incident, finding)),
                true) is SupplyWatchStep.Resolve),
        ];

        return new SupplyWatchPlan(opening, resolving);
    }

    private static bool Watched(QualityIncident incident)
        => incident.Breached is QualityThresholdKey.SupplySilence && !incident.HasSettled;

    private static bool About(QualityIncident incident, SupplySilenceFinding finding)
        => incident.Silence == finding.Silence && incident.Subject.Equals(finding.Subject);
}
