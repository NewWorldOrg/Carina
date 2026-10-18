namespace Carina.Domain.Segments;

/// <summary>
/// The settings learning tries: each set of thresholds the segments are read with, and with each, every
/// pull towards zero on the weights (<see cref="Lambdas"/>) beside every pull on the station and series
/// offsets alike (<see cref="OffsetLambdas"/>), in that order. <see cref="Default"/> reads with the
/// initial thresholds and with each of silence, black, a change of scene and the tolerance of the grid
/// moved one step either way, the others kept; it tries the weights' pulls 1, 0.1 and 10 and the offsets'
/// 10, 1 and 100, so that its first setting is the initial one.
/// </summary>
public sealed class SegmentSearch
{
    public SegmentSearch(IReadOnlyList<SegmentThresholds> readings, IReadOnlyList<double> lambdas, IReadOnlyList<double> offsetLambdas)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(lambdas);
        ArgumentNullException.ThrowIfNull(offsetLambdas);

        if (readings.Count is 0 || lambdas.Count is 0 || offsetLambdas.Count is 0 || readings.Any(reading => reading is null))
        {
            throw new ArgumentException("A search tries at least one reading and one pull of each.", nameof(readings));
        }

        Readings = [.. readings];
        Lambdas = [.. lambdas];
        OffsetLambdas = [.. offsetLambdas];
        Settings =
        [
            .. Readings.SelectMany(reading => Lambdas.SelectMany(lambda => OffsetLambdas.Select(offsets => new SegmentThresholds(
                reading.SilenceDbfs,
                reading.BlackLevel,
                reading.SceneChange,
                reading.GridToleranceSec,
                reading.GridPenalty,
                lambda,
                offsets,
                offsets)))),
        ];
    }

    public static SegmentSearch Default { get; } = new(
        [
            SegmentThresholds.Initial,
            Moved(silenceDbfs: -55),
            Moved(silenceDbfs: -45),
            Moved(blackLevel: 0.08),
            Moved(blackLevel: 0.12),
            Moved(sceneChange: 0.25),
            Moved(sceneChange: 0.35),
            Moved(gridToleranceSec: 0.5),
            Moved(gridToleranceSec: 1.5),
        ],
        [1, 0.1, 10],
        [10, 1, 100]);

    public IReadOnlyList<SegmentThresholds> Readings { get; }

    public IReadOnlyList<double> Lambdas { get; }

    public IReadOnlyList<double> OffsetLambdas { get; }

    public IReadOnlyList<SegmentThresholds> Settings { get; }

    private static SegmentThresholds Moved(double? silenceDbfs = null, double? blackLevel = null, double? sceneChange = null, double? gridToleranceSec = null)
    {
        SegmentThresholds initial = SegmentThresholds.Initial;

        return new SegmentThresholds(
            silenceDbfs ?? initial.SilenceDbfs,
            blackLevel ?? initial.BlackLevel,
            sceneChange ?? initial.SceneChange,
            gridToleranceSec ?? initial.GridToleranceSec,
            initial.GridPenalty,
            initial.Lambda,
            initial.LambdaStation,
            initial.LambdaSeries);
    }
}
