namespace Carina.Domain.Quality;

public sealed record QualityObservation
{
    private QualityObservation(QualityFacet facet, QualityStanding standing, double? observed)
    {
        Facet = facet;
        Standing = standing;
        Observed = observed;
    }

    public QualityFacet Facet { get; }

    public QualityStanding Standing { get; }

    public double? Observed { get; }

    public bool WasMeasured => QualityStandings.WasMeasured(Standing);

    public static QualityObservation Of(QualityFacet facet, ThresholdVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(facet);
        ArgumentNullException.ThrowIfNull(verdict);

        return new QualityObservation(facet, verdict.Standing, verdict.Observed);
    }

    public static QualityObservation Unsupported(QualityFacet facet) => Without(facet, QualityStanding.Unsupported);

    public static QualityObservation Unreachable(QualityFacet facet) => Without(facet, QualityStanding.Unreachable);

    private static QualityObservation Without(QualityFacet facet, QualityStanding standing)
    {
        ArgumentNullException.ThrowIfNull(facet);

        return new QualityObservation(facet, standing, null);
    }
}
