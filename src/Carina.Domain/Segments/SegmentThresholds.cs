using System.Runtime.CompilerServices;

namespace Carina.Domain.Segments;

/// <summary>
/// The thresholds a version of the model reads with: how quiet sound is to count as silence, in dBFS;
/// how dark a frame is to count as black and how far it moves from the frame before to count as a change
/// of scene, both as shares of full scale; how far off the 15-second grid a run of breaks may end, in
/// seconds, and what leaving a break off it costs; and how strongly learning pulls the weights, the
/// station offsets and the series offsets towards the version in use.
/// </summary>
public sealed record SegmentThresholds
{
    public const double QuietestDbfs = -SoundLoudness.Silent * SoundLoudness.StepDecibels;

    public SegmentThresholds(
        double silenceDbfs,
        double blackLevel,
        double sceneChange,
        double gridToleranceSec,
        double gridPenalty,
        double lambda,
        double lambdaStation,
        double lambdaSeries)
    {
        SilenceDbfs = Within(silenceDbfs, value => value > QuietestDbfs && value < 0, "Silence lies below full scale and above what the loudness reaches.");
        BlackLevel = Within(blackLevel, IsAShare, "A level of black is a share of full scale above none of it.");
        SceneChange = Within(sceneChange, IsAShare, "A change of scene is a share of full scale above none of it.");
        GridToleranceSec = Within(
            gridToleranceSec,
            value => value >= 0 && value < CatalogKinds.Grid.TotalSeconds / 2,
            "How far off the grid a break may end is no less than nothing and less than half the grid.");
        GridPenalty = Within(gridPenalty, value => value <= 0, "Leaving a break off the grid costs something or nothing.");
        Lambda = Within(lambda, IsNoLessThanNothing, "A pull is no less than nothing.");
        LambdaStation = Within(lambdaStation, IsNoLessThanNothing, "A pull is no less than nothing.");
        LambdaSeries = Within(lambdaSeries, IsNoLessThanNothing, "A pull is no less than nothing.");
    }

    public static SegmentThresholds Initial { get; } = new(-50, 0.10, 0.30, 1.0, -3.0, 1, 10, 10);

    public double SilenceDbfs { get; }

    public double BlackLevel { get; }

    public double SceneChange { get; }

    public double GridToleranceSec { get; }

    public double GridPenalty { get; }

    public double Lambda { get; }

    public double LambdaStation { get; }

    public double LambdaSeries { get; }

    private static bool IsAShare(double value) => value is > 0 and <= 1;

    private static bool IsNoLessThanNothing(double value) => value >= 0;

    private static double Within(
        double value,
        Func<double, bool> holds,
        string meaning,
        [CallerArgumentExpression(nameof(value))] string? named = null)
        => double.IsFinite(value) && holds(value) ? value : throw new ArgumentOutOfRangeException(named, value, meaning);
}
