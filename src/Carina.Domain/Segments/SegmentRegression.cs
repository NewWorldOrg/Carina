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
/// the opening and the ending each against the programme. Of the opening and the ending, one no sample labels
/// can be kept from a version that weighs both, with the scales of the features it weighs: only the kinds
/// labelled and the break are fitted then, on those scales, and the kept weights are handed on as they were.
/// Without a version to keep it from, a kind no sample labels is fitted from none. The parameters are an
/// intercept and a weight per feature of <see cref="SegmentFeature.Names"/> for each kind fitted, and an offset
/// per station and one per series named by the samples, which weigh the break alone. Each step solves
/// <c>(XᵀWX + Λ) Δθ = Xᵀ(c ⊙ (y − p)) − Λθ</c> by a Cholesky decomposition, where <c>W</c> holds
/// <c>c (p_k δ_kl − p_k p_l)</c> between the kinds weighed, which is <c>c p (1 − p)</c> with two, and <c>Λ</c>
/// carries twice each pull of the thresholds, since the loss pulls by <c>λ‖w − w₀‖² + λ_u Σ (u − u₀)² + λ_v Σ (v − v₀)²</c>.
/// The parameters are pulled towards a version handed over, the version in use, its weights moved onto the scales the
/// samples standardise by so that they score each feature as it does; towards zero where it has no weight or offset
/// of that name, or when none is handed over. Its offset of a station or a series no sample is on is handed on as it is. A kind with few labels so stays near that version, and the more labels
/// there are the further the samples move it. The intercepts are not pulled: how often each kind comes among the
/// samples is told by the samples even when they label it seldom. With two kinds every step is the one the two-kind
/// fit took, to the last bit. A step that would lower
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

    public static SegmentFit Fit(
        IReadOnlyList<SegmentSample> samples,
        SegmentThresholds thresholds,
        SegmentModel? start = null,
        SegmentModel? kept = null,
        SegmentModel? toward = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (samples.Any(sample => sample is null))
        {
            throw new ArgumentException("Every sample is handed over.", nameof(samples));
        }

        if (kept is not null && kept.Kinds is not 4)
        {
            throw new ArgumentException("The opening and the ending are kept from a version that weighs them.", nameof(kept));
        }

        Design design = Design.Of(samples, kept, toward);
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

        private KeyValuePair<SegmentKind, SegmentClassWeights>[] kept = [];

        private string[] stations = [];

        private string[] series = [];

        private FeatureScale[] scales = [];

        private double[] values = [];

        private int[] stationOf = [];

        private int[] seriesOf = [];

        private int[] labels = [];

        private double[] weights = [];

        private double[] target = [];

        private KeyValuePair<string, double>[] stationsUnseen = [];

        private KeyValuePair<string, double>[] seriesUnseen = [];

        public int Width => Offsets + stations.Length + series.Length;

        private int Count => labels.Length;

        private int Features => names.Count;

        private int Block => 1 + Features;

        private int Offsets => classes.Length * Block;

        public static Design Of(IReadOnlyList<SegmentSample> samples, SegmentModel? keeping, SegmentModel? toward)
        {
            SegmentKind[] classes = Fitted(samples, keeping);
            KeyValuePair<SegmentKind, SegmentClassWeights>[] kept =
            [
                .. keeping?.Weights.Classes.Where(pair => pair.Key is not SegmentKind.Break && !classes.Contains(pair.Key)) ?? [],
            ];
            HashSet<string> keptScales = [.. kept.SelectMany(pair => pair.Value.Features.Keys)];
            Design design = new()
            {
                classes = classes,
                kept = kept,
                stations = [.. samples.Select(sample => sample.Station).Distinct().Order(StringComparer.Ordinal)],
                series = [.. samples.Select(sample => sample.Series).OfType<string>().Distinct().Order(StringComparer.Ordinal)],
                labels = [.. samples.Select(sample => Array.IndexOf(classes, sample.Kind))],
                weights = [.. samples.Select(sample => sample.Weight)],
            };

            design.scales = [.. design.names.Select(name => keptScales.Contains(name) ? keeping!.Standardisation[name] : Scale(samples, name))];
            design.stationOf = [.. samples.Select(sample => design.Offsets + Array.BinarySearch(design.stations, sample.Station, StringComparer.Ordinal))];
            design.seriesOf = [.. samples.Select(sample => sample.Series is string named ? design.SeriesColumn(named) : -1)];
            design.values = new double[samples.Count * design.Features];

            for (int sample = 0; sample < samples.Count; sample++)
            {
                design.Standardise(sample, samples[sample].Features);
            }

            design.target = toward is null ? new double[design.Width] : design.Target(toward);
            design.stationsUnseen = [.. toward?.StationOffsets.Where(pair => !design.stations.Contains(pair.Key)) ?? []];
            design.seriesUnseen = [.. toward?.SeriesOffsets.Where(pair => !design.series.Contains(pair.Key)) ?? []];

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
                double apart = theta[at] - target[at];

                penalty += pull[at] * apart * apart;
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

            int[] used = new int[classes.Length];

            for (int sample = 0; sample < Count; sample++)
            {
                Scores(theta, sample, scores);
                (double highest, double sum) = Normaliser(scores);

                for (int weighed = 0; weighed < classes.Length; weighed++)
                {
                    chances[weighed] = Math.Exp(scores[weighed] - highest) / sum;
                    used[weighed] = Row(sample, weighed, columns[weighed], entries[weighed]);
                }

                for (int weighed = 0; weighed < classes.Length; weighed++)
                {
                    double residual = weights[sample] * ((labels[sample] == weighed ? 1.0 : 0.0) - chances[weighed]);

                    for (int one = 0; one < used[weighed]; one++)
                    {
                        gradient[columns[weighed][one]] += residual * entries[weighed][one];
                    }

                    for (int other = 0; other < classes.Length; other++)
                    {
                        double coefficient = other == weighed
                            ? weights[sample] * chances[weighed] * (1 - chances[weighed])
                            : -(weights[sample] * chances[weighed] * chances[other]);

                        Accumulate(hessian, width, coefficient, (columns[weighed], entries[weighed], used[weighed]), (columns[other], entries[other], used[other]));
                    }
                }
            }

            for (int at = 0; at < width; at++)
            {
                gradient[at] -= pull[at] * (theta[at] - target[at]);
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
                            names.Select((name, feature) => (name, feature)).ToDictionary(named => named.name, named => theta[(pair.weighed * Block) + 1 + named.feature])))
                    .Concat(kept)
                    .ToDictionary()),
                stations.Select((station, at) => KeyValuePair.Create(station, theta[Offsets + at])).Concat(stationsUnseen).ToDictionary(),
                series.Select((named, at) => KeyValuePair.Create(named, theta[Offsets + stations.Length + at])).Concat(seriesUnseen).ToDictionary(),
                thresholds,
                names.Select((name, feature) => (name, feature)).ToDictionary(pair => pair.name, pair => scales[pair.feature]));

        /// <summary>
        /// Where each parameter is pulled to: each weight of <paramref name="toward"/> on a kind fitted, moved onto the
        /// scale the samples standardise its feature by so that it scores the feature as <paramref name="toward"/> does,
        /// and its offset of each station and series; zero for a weight or an offset it does not have. The intercepts are
        /// not pulled.
        /// </summary>
        private double[] Target(SegmentModel toward)
        {
            double[] pulledTo = new double[Width];

            for (int weighed = 0; weighed < classes.Length; weighed++)
            {
                if (!toward.Weights.Classes.TryGetValue(classes[weighed], out SegmentClassWeights? given))
                {
                    continue;
                }

                for (int feature = 0; feature < Features; feature++)
                {
                    if (given.Features.TryGetValue(names[feature], out double weight))
                    {
                        pulledTo[(weighed * Block) + 1 + feature] = weight * scales[feature].Sd / toward.Standardisation[names[feature]].Sd;
                    }
                }
            }

            for (int station = 0; station < stations.Length; station++)
            {
                pulledTo[Offsets + station] = toward.StationOffsets.GetValueOrDefault(stations[station]);
            }

            for (int named = 0; named < series.Length; named++)
            {
                pulledTo[Offsets + stations.Length + named] = toward.SeriesOffsets.GetValueOrDefault(series[named]);
            }

            return pulledTo;
        }

        /// <summary>
        /// The kinds fitted: the break alone when no sample labels an opening or an ending; otherwise the break, the
        /// opening and the ending, but for one no sample labels when it is kept from <paramref name="keeping"/>.
        /// </summary>
        private static SegmentKind[] Fitted(IReadOnlyList<SegmentSample> samples, SegmentModel? keeping)
        {
            SegmentKind[] labelled = [.. new[] { SegmentKind.Opening, SegmentKind.Ending }.Where(kind => samples.Any(sample => sample.Kind == kind))];

            if (labelled.Length is 0)
            {
                return [SegmentKind.Break];
            }

            return keeping is null ? [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending] : [SegmentKind.Break, .. labelled];
        }

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
        /// Adds <paramref name="coefficient"/> times the outer product of two rows, each its columns, entries and how
        /// many of them are used, to the upper triangle of <paramref name="hessian"/>, where a row's column is no later
        /// than the other's.
        /// </summary>
        private static void Accumulate(double[] hessian, int width, double coefficient, (int[] Columns, double[] Entries, int Used) row, (int[] Columns, double[] Entries, int Used) other)
        {
            for (int one = 0; one < row.Used; one++)
            {
                for (int against = 0; against < other.Used; against++)
                {
                    if (row.Columns[one] <= other.Columns[against])
                    {
                        hessian[(row.Columns[one] * width) + other.Columns[against]] += coefficient * row.Entries[one] * other.Entries[against];
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

            if (weighed is not 0)
            {
                return Features + 1;
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
                double score = theta[weighed * Block];

                if (weighed is 0)
                {
                    score += theta[stationOf[sample]];
                    score += seriesOf[sample] >= 0 ? theta[seriesOf[sample]] : 0;
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
