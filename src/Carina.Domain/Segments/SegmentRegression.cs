namespace Carina.Domain.Segments;

/// <summary>
/// A model fitted to labelled samples, how many Newton steps it took, and whether the penalised
/// log-likelihood came to rest before the steps ran out.
/// </summary>
public sealed record SegmentFit(SegmentModel Model, int Iterations, bool Converged);

/// <summary>
/// Fits the logistic regression telling the kinds of segment apart to labelled samples, by Newton's method
/// (IRLS). Labels of the programme and of breaks alone are fitted as two kinds, a break against the programme;
/// an opening or an ending among them makes it four, a multinomial logistic regression (softmax) of the break,
/// the opening and the ending each against the programme. The parameters are an intercept and a weight per
/// feature of <see cref="SegmentFeature.Names"/> for each kind weighed, and an offset per station and one per
/// series named by the samples, which every kind weighed shares. Each step solves
/// <c>(XᵀWX + Λ) Δθ = Xᵀ(c ⊙ (y − p)) − Λθ</c> by a Cholesky decomposition, where <c>W</c> holds
/// <c>c (p_k δ_kl − p_k p_l)</c> between the kinds weighed, which is <c>c p (1 − p)</c> with two, and <c>Λ</c>
/// carries twice each pull of the thresholds, since the loss pulls by <c>λ‖w‖² + λ_u Σ u² + λ_v Σ v²</c>; the
/// intercepts are not pulled. With two kinds every step is the one the two-kind fit took, to the last bit. A step that would lower
/// the penalised log-likelihood is halved until it does not. The fit stops once a step changes the
/// penalised log-likelihood by less than <see cref="SmallestChange"/>, after <see cref="MostIterations"/>
/// steps, or when the system cannot be solved, as with no samples. Each feature is standardised by the
/// mean and standard deviation of the samples it is known in, a deviation of nothing standing as one, and a
/// feature not known in a sample counts as its mean. Nothing is drawn at random: the same samples in the
/// same order give the same model.
/// </summary>
public static class SegmentRegression
{
    public const int MostIterations = 50;

    public const double SmallestChange = 1e-6;

    private const int MostHalvings = 30;

    private const double FlatDeviation = 1e-9;

    private const double SingularPivot = 1e-12;

    public static SegmentFit Fit(IReadOnlyList<SegmentSample> samples, SegmentThresholds thresholds, SegmentModel? start = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (samples.Any(sample => sample is null))
        {
            throw new ArgumentException("Every sample is handed over.", nameof(samples));
        }

        Design design = Design.Of(samples);
        double[] pull = design.Pull(thresholds);
        double[] theta = start is null ? new double[design.Width] : design.From(start);
        double objective = design.Objective(theta, pull);
        int iterations = 0;
        bool converged = false;

        while (!converged && iterations < MostIterations)
        {
            double[]? step = design.Step(theta, pull);

            if (step is null)
            {
                break;
            }

            iterations++;
            (double[] walked, double reached) = Walked(design, theta, step, pull, objective);
            converged = Math.Abs(reached - objective) < SmallestChange;
            theta = walked;
            objective = reached;
        }

        return new SegmentFit(design.Model(theta, thresholds), iterations, converged);
    }

    private static (double[] Theta, double Objective) Walked(Design design, double[] theta, double[] step, double[] pull, double objective)
    {
        double scale = 1;
        double[] walked = Moved(theta, step, scale);
        double reached = design.Objective(walked, pull);

        for (int halving = 0; halving < MostHalvings && !(reached >= objective); halving++)
        {
            scale /= 2;
            walked = Moved(theta, step, scale);
            reached = design.Objective(walked, pull);
        }

        return (walked, reached);
    }

    private static double[] Moved(double[] theta, double[] step, double scale)
    {
        double[] moved = new double[theta.Length];

        for (int at = 0; at < theta.Length; at++)
        {
            moved[at] = theta[at] + (scale * step[at]);
        }

        return moved;
    }

    /// <summary>
    /// The highest of <paramref name="scores"/> and of the programme's nothing, and the sum of the exponentials of
    /// every score, the programme's first, less it: what the softmax is normalised by.
    /// </summary>
    private static (double Highest, double Sum) Normaliser(double[] scores)
    {
        double highest = 0;

        foreach (double score in scores)
        {
            if (score > highest)
            {
                highest = score;
            }
        }

        double sum = Math.Exp(0 - highest);

        foreach (double score in scores)
        {
            sum += Math.Exp(score - highest);
        }

        return (highest, sum);
    }

    /// <summary>
    /// Solves <c>h x = g</c> for a symmetric <paramref name="h"/> of <paramref name="width"/> columns by its
    /// Cholesky decomposition, or answers null when it is not positive definite.
    /// </summary>
    private static double[]? Solved(double[] h, double[] g, int width)
    {
        double[] lower = new double[width * width];

        for (int column = 0; column < width; column++)
        {
            double diagonal = h[(column * width) + column];
            double pivot = diagonal - Dot(lower, column * width, column * width, column);

            if (!(pivot > SingularPivot * Math.Abs(diagonal)))
            {
                return null;
            }

            lower[(column * width) + column] = Math.Sqrt(pivot);

            for (int row = column + 1; row < width; row++)
            {
                lower[(row * width) + column] = (h[(row * width) + column] - Dot(lower, row * width, column * width, column)) / lower[(column * width) + column];
            }
        }

        double[] forward = new double[width];

        for (int row = 0; row < width; row++)
        {
            forward[row] = (g[row] - Dot(lower, row * width, forward, row)) / lower[(row * width) + row];
        }

        double[] solved = new double[width];

        for (int row = width - 1; row >= 0; row--)
        {
            double sum = 0;

            for (int below = row + 1; below < width; below++)
            {
                sum += lower[(below * width) + row] * solved[below];
            }

            solved[row] = (forward[row] - sum) / lower[(row * width) + row];
        }

        return solved;
    }

    private static double Dot(double[] matrix, int one, int other, int count)
    {
        double sum = 0;

        for (int at = 0; at < count; at++)
        {
            sum += matrix[one + at] * matrix[other + at];
        }

        return sum;
    }

    private static double Dot(double[] matrix, int row, double[] vector, int count)
    {
        double sum = 0;

        for (int at = 0; at < count; at++)
        {
            sum += matrix[row + at] * vector[at];
        }

        return sum;
    }

    private sealed class Design
    {
        private readonly IReadOnlyList<string> names = SegmentFeature.Names;

        private SegmentKind[] classes = [];

        private string[] stations = [];

        private string[] series = [];

        private FeatureScale[] scales = [];

        private double[] values = [];

        private int[] stationOf = [];

        private int[] seriesOf = [];

        private int[] labels = [];

        private double[] weights = [];

        public int Width => Offsets + stations.Length + series.Length;

        private int Count => labels.Length;

        private int Features => names.Count;

        private int Block => 1 + Features;

        private int Offsets => classes.Length * Block;

        public static Design Of(IReadOnlyList<SegmentSample> samples)
        {
            SegmentKind[] classes = samples.Any(sample => sample.Kind is SegmentKind.Opening or SegmentKind.Ending)
                ? [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending]
                : [SegmentKind.Break];
            Design design = new()
            {
                classes = classes,
                stations = [.. samples.Select(sample => sample.Station).Distinct().Order(StringComparer.Ordinal)],
                series = [.. samples.Select(sample => sample.Series).OfType<string>().Distinct().Order(StringComparer.Ordinal)],
                labels = [.. samples.Select(sample => Array.IndexOf(classes, sample.Kind))],
                weights = [.. samples.Select(sample => sample.Weight)],
            };

            design.scales = [.. design.names.Select(name => Scale(samples, name))];
            design.stationOf = [.. samples.Select(sample => design.Offsets + Array.BinarySearch(design.stations, sample.Station, StringComparer.Ordinal))];
            design.seriesOf = [.. samples.Select(sample => sample.Series is string named ? design.SeriesColumn(named) : -1)];
            design.values = new double[samples.Count * design.Features];

            for (int sample = 0; sample < samples.Count; sample++)
            {
                design.Standardise(sample, samples[sample].Features);
            }

            return design;
        }

        public double[] Pull(SegmentThresholds thresholds)
        {
            double[] pull = new double[Width];

            for (int weighed = 0; weighed < classes.Length; weighed++)
            {
                Array.Fill(pull, 2 * thresholds.Lambda, (weighed * Block) + 1, Features);
            }

            Array.Fill(pull, 2 * thresholds.LambdaStation, Offsets, stations.Length);
            Array.Fill(pull, 2 * thresholds.LambdaSeries, Offsets + stations.Length, series.Length);

            return pull;
        }

        public double[] From(SegmentModel start)
        {
            double[] theta = new double[Width];

            for (int weighed = 0; weighed < classes.Length; weighed++)
            {
                if (!start.Weights.Classes.TryGetValue(classes[weighed], out SegmentClassWeights? given))
                {
                    continue;
                }

                theta[weighed * Block] = given.Bias;

                for (int feature = 0; feature < Features; feature++)
                {
                    theta[(weighed * Block) + 1 + feature] = given.Features.GetValueOrDefault(names[feature]);
                }
            }

            for (int station = 0; station < stations.Length; station++)
            {
                theta[Offsets + station] = start.StationOffsets.GetValueOrDefault(stations[station]);
            }

            for (int named = 0; named < series.Length; named++)
            {
                theta[Offsets + stations.Length + named] = start.SeriesOffsets.GetValueOrDefault(series[named]);
            }

            return theta;
        }

        public double Objective(double[] theta, double[] pull)
        {
            double likelihood = 0;
            double[] scores = new double[classes.Length];

            for (int sample = 0; sample < Count; sample++)
            {
                Scores(theta, sample, scores);
                (double highest, double sum) = Normaliser(scores);
                double labelled = labels[sample] < 0 ? 0 : scores[labels[sample]];

                likelihood += weights[sample] * ((labelled - highest) - Math.Log(sum));
            }

            double penalty = 0;

            for (int at = 0; at < theta.Length; at++)
            {
                penalty += pull[at] * theta[at] * theta[at];
            }

            return likelihood - (penalty / 2);
        }

        public double[]? Step(double[] theta, double[] pull)
        {
            int width = Width;
            double[] hessian = new double[width * width];
            double[] gradient = new double[width];
            int[][] columns = [.. classes.Select(_ => new int[Features + 3])];
            double[][] entries = [.. classes.Select(_ => new double[Features + 3])];
            double[] scores = new double[classes.Length];
            double[] chances = new double[classes.Length];

            for (int sample = 0; sample < Count; sample++)
            {
                Scores(theta, sample, scores);
                (double highest, double sum) = Normaliser(scores);
                int used = 0;

                for (int weighed = 0; weighed < classes.Length; weighed++)
                {
                    chances[weighed] = Math.Exp(scores[weighed] - highest) / sum;
                    used = Row(sample, weighed, columns[weighed], entries[weighed]);
                }

                for (int weighed = 0; weighed < classes.Length; weighed++)
                {
                    double residual = weights[sample] * ((labels[sample] == weighed ? 1.0 : 0.0) - chances[weighed]);

                    for (int one = 0; one < used; one++)
                    {
                        gradient[columns[weighed][one]] += residual * entries[weighed][one];
                    }

                    for (int other = 0; other < classes.Length; other++)
                    {
                        double coefficient = other == weighed
                            ? weights[sample] * chances[weighed] * (1 - chances[weighed])
                            : -(weights[sample] * chances[weighed] * chances[other]);

                        Accumulate(hessian, width, coefficient, columns[weighed], entries[weighed], columns[other], entries[other], used);
                    }
                }
            }

            for (int at = 0; at < width; at++)
            {
                gradient[at] -= pull[at] * theta[at];
                hessian[(at * width) + at] += pull[at];
            }

            Mirrored(hessian, width);

            return Solved(hessian, gradient, width);
        }

        public SegmentModel Model(double[] theta, SegmentThresholds thresholds)
            => new(
                new SegmentWeights(classes
                    .Select((kind, weighed) => (kind, weighed))
                    .ToDictionary(
                        pair => pair.kind,
                        pair => new SegmentClassWeights(
                            theta[pair.weighed * Block],
                            names.Select((name, feature) => (name, feature)).ToDictionary(named => named.name, named => theta[(pair.weighed * Block) + 1 + named.feature])))),
                stations.Select((station, at) => (station, at)).ToDictionary(pair => pair.station, pair => theta[Offsets + pair.at]),
                series.Select((named, at) => (named, at)).ToDictionary(pair => pair.named, pair => theta[Offsets + stations.Length + pair.at]),
                thresholds,
                names.Select((name, feature) => (name, feature)).ToDictionary(pair => pair.name, pair => scales[pair.feature]));

        private static FeatureScale Scale(IReadOnlyList<SegmentSample> samples, string name)
        {
            double[] known = [.. samples.Select(sample => sample.Features[name]).OfType<double>()];

            if (known.Length is 0)
            {
                return FeatureScale.Identity;
            }

            double mean = known.Average();
            double deviation = Math.Sqrt(known.Sum(value => (value - mean) * (value - mean)) / known.Length);

            return new FeatureScale(mean, deviation > FlatDeviation * Math.Max(1, Math.Abs(mean)) ? deviation : 1);
        }

        /// <summary>
        /// Adds <paramref name="coefficient"/> times the outer product of two rows to the upper triangle of
        /// <paramref name="hessian"/>, where a row's column is no later than the other's.
        /// </summary>
        private static void Accumulate(double[] hessian, int width, double coefficient, int[] columns, double[] entries, int[] otherColumns, double[] otherEntries, int used)
        {
            for (int one = 0; one < used; one++)
            {
                for (int other = 0; other < used; other++)
                {
                    if (columns[one] <= otherColumns[other])
                    {
                        hessian[(columns[one] * width) + otherColumns[other]] += coefficient * entries[one] * otherEntries[other];
                    }
                }
            }
        }

        private static void Mirrored(double[] matrix, int width)
        {
            for (int row = 1; row < width; row++)
            {
                for (int column = 0; column < row; column++)
                {
                    matrix[(row * width) + column] = matrix[(column * width) + row];
                }
            }
        }

        private int SeriesColumn(string named) => Offsets + stations.Length + Array.BinarySearch(series, named, StringComparer.Ordinal);

        private void Standardise(int sample, SegmentFeatureValues features)
        {
            for (int feature = 0; feature < Features; feature++)
            {
                values[(sample * Features) + feature] = features[names[feature]] is double value ? scales[feature].Standardised(value) : 0;
            }
        }

        private int Row(int sample, int weighed, int[] columns, double[] entries)
        {
            columns[0] = weighed * Block;
            entries[0] = 1;

            for (int feature = 0; feature < Features; feature++)
            {
                columns[1 + feature] = (weighed * Block) + 1 + feature;
                entries[1 + feature] = values[(sample * Features) + feature];
            }

            columns[1 + Features] = stationOf[sample];
            entries[1 + Features] = 1;

            if (seriesOf[sample] < 0)
            {
                return Features + 2;
            }

            columns[2 + Features] = seriesOf[sample];
            entries[2 + Features] = 1;

            return Features + 3;
        }

        private void Scores(double[] theta, int sample, double[] scores)
        {
            for (int weighed = 0; weighed < classes.Length; weighed++)
            {
                double score = theta[weighed * Block] + theta[stationOf[sample]];

                if (seriesOf[sample] >= 0)
                {
                    score += theta[seriesOf[sample]];
                }

                for (int feature = 0; feature < Features; feature++)
                {
                    score += theta[(weighed * Block) + 1 + feature] * values[(sample * Features) + feature];
                }

                scores[weighed] = score;
            }
        }
    }
}
