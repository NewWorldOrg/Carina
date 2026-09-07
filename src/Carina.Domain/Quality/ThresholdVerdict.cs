namespace Carina.Domain.Quality;

public sealed record ThresholdVerdict
{
    private ThresholdVerdict(
        QualityStanding standing,
        double? observed,
        QualityThresholdKey appliedKey,
        Threshold applied,
        QualityThresholdKey? breached)
    {
        Standing = standing;
        Observed = observed;
        AppliedKey = appliedKey;
        Applied = applied;
        Breached = breached;
    }

    public QualityStanding Standing { get; }

    public double? Observed { get; }

    public QualityThresholdKey AppliedKey { get; }

    public Threshold Applied { get; }

    public QualityThresholdKey? Breached { get; }

    public bool Provisional => Applied.Provisional;

    internal static ThresholdVerdict Of(
        QualityStanding standing,
        double? observed,
        QualityThresholdKey appliedKey,
        Threshold applied)
        => new(
            standing,
            observed,
            appliedKey,
            applied,
            QualityStandings.WentBeyond(standing) ? appliedKey : null);
}
