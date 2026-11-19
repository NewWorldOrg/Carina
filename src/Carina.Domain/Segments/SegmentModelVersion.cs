using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public enum SegmentModelVersionState
{
    Candidate = 1,

    Active = 2,

    Retired = 3,
}

public sealed class SegmentModelVersion
{
    public const int FirstNumber = 1;

    private SegmentModel? model;

    private SegmentModelVersion()
    {
    }

    public int Number { get; private set; }

    public SegmentModelVersionState State { get; private set; }

    public bool ByHand { get; private set; }

    public bool BuiltIn { get; private set; }

    public int Kinds { get; private set; }

    public SegmentWeights Weights { get; private set; } = null!;

    public IReadOnlyDictionary<string, double> StationOffsets { get; private set; } = null!;

    public IReadOnlyDictionary<string, double> SeriesOffsets { get; private set; } = null!;

    public SegmentThresholds Thresholds { get; private set; } = null!;

    public IReadOnlyDictionary<string, FeatureScale> Standardisation { get; private set; } = null!;

    public DateTime TrainedAt { get; private set; }

    public DateTime? AdoptedAt { get; private set; }

    public DateTime? RetiredAt { get; private set; }

    public DateTime? ChosenByHandAt { get; private set; }

    public DateTime? RolledBackAt { get; private set; }

    public SegmentRollback? Rollback { get; private set; }

    public SegmentMetrics? Metrics { get; private set; }

    public IReadOnlyList<StationMetrics>? Stations { get; private set; }

    public EvaluationEvidence? Evidence { get; private set; }

    public SegmentModel Model => model ??= new SegmentModel(Weights, StationOffsets, SeriesOffsets, Thresholds, Standardisation);

    public SegmentEvaluation? Evaluation => Evidence is null ? null : new SegmentEvaluation(Metrics, Stations ?? [], Evidence);

    private bool NeverUsed => State is SegmentModelVersionState.Candidate && AdoptedAt is null && ChosenByHandAt is null;

    public bool IsLetGoBefore(int latest, bool named) => Number < latest && NeverUsed && !BuiltIn && !named;

    public static SegmentModelVersion Rehydrate(
        int number,
        SegmentModelVersionState state,
        bool byHand,
        SegmentModel model,
        DateTime trainedAt,
        DateTime? adoptedAt,
        DateTime? retiredAt,
        DateTime? chosenByHandAt,
        DateTime? rolledBackAt,
        SegmentEvaluation? evaluation = null,
        SegmentRollback? rollback = null,
        bool builtIn = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, FirstNumber);
        ArgumentNullException.ThrowIfNull(model);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A version stands in one of the three states kept.");
        }

        DateTime trained = UtcTimes.Required(trainedAt, nameof(trainedAt));
        DateTime? adopted = UtcTimes.Optional(adoptedAt, nameof(adoptedAt));
        DateTime? retired = UtcTimes.Optional(retiredAt, nameof(retiredAt));
        DateTime? chosen = UtcTimes.Optional(chosenByHandAt, nameof(chosenByHandAt));
        DateTime? rolledBack = UtcTimes.Optional(rolledBackAt, nameof(rolledBackAt));
        string? fault = Fault(state, byHand, trained, adopted, retired, chosen, rolledBack)
                        ?? RollbackFault(rolledBack, rollback)
                        ?? (builtIn && evaluation is not null ? "A version built in is never evaluated." : null);

        if (fault is not null)
        {
            throw new ArgumentException(fault);
        }

        return new SegmentModelVersion
        {
            Number = number,
            State = state,
            ByHand = byHand,
            BuiltIn = builtIn,
            Kinds = model.Kinds,
            Weights = model.Weights,
            StationOffsets = model.StationOffsets,
            SeriesOffsets = model.SeriesOffsets,
            Thresholds = model.Thresholds,
            Standardisation = model.Standardisation,
            TrainedAt = trained,
            AdoptedAt = adopted,
            RetiredAt = retired,
            ChosenByHandAt = chosen,
            RolledBackAt = rolledBack,
            Rollback = rollback,
            Metrics = evaluation?.Metrics,
            Stations = evaluation is null ? null : [.. evaluation.Stations],
            Evidence = evaluation?.Evidence,
            model = model,
        };
    }

    public static SegmentModelVersion Learned(int number, SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return Rehydrate(number, SegmentModelVersionState.Candidate, false, model, trainedAt, null, null, null, null, evaluation);
    }

    public static int NumberAfter(int highestGiven)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(highestGiven);

        return Math.Max(highestGiven + 1, FirstNumber);
    }

    public static IReadOnlyList<int> LetGoBefore(IReadOnlyList<SegmentModelVersion> kept, int latest)
        => LetGoBefore(kept, latest, new HashSet<int>());

    public static IReadOnlyList<int> LetGoBefore(IReadOnlyList<SegmentModelVersion> kept, int latest, IReadOnlySet<int> named)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(named);

        return
        [
            .. kept
                .Where(version => version.IsLetGoBefore(latest, named.Contains(version.Number)))
                .Select(version => version.Number)
                .Order(),
        ];
    }

    public static SegmentBuiltInKept? KeepBuiltIn(IReadOnlyList<SegmentModelVersion> kept, SegmentModel builtIn, DateTime at, int highestGiven = 0)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(builtIn);

        if (kept.Any(version => version.BuiltIn && version.Model.Equals(builtIn)))
        {
            return null;
        }

        int number = NumberAfter(Math.Max(highestGiven, kept.Count is 0 ? 0 : kept.Max(version => version.Number)));

        if (kept.SingleOrDefault(version => version.State is SegmentModelVersionState.Active) is not { } inUse)
        {
            SegmentModelVersion first = Rehydrate(number, SegmentModelVersionState.Active, false, builtIn, at, at, null, null, null, builtIn: true);

            return new SegmentBuiltInKept(first, [], first);
        }

        SegmentModelVersion candidate = Rehydrate(number, SegmentModelVersionState.Candidate, false, builtIn, at, null, null, null, null, builtIn: true);

        if (!inUse.BuiltIn || inUse.ByHand)
        {
            return new SegmentBuiltInKept(candidate, [], inUse);
        }

        SegmentVersionChanged changed = Moved(inUse, candidate, SegmentVersionChange.Adopting(number), at);

        return new SegmentBuiltInKept(changed.Active, [.. changed.Written.Where(version => version.Number != number)], changed.Active);
    }

    public static SegmentVersionChanged? Change(IReadOnlyList<SegmentModelVersion> kept, SegmentVersionChange change, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(change);

        SegmentModelVersion[] active = [.. kept.Where(version => version.State is SegmentModelVersionState.Active)];

        if (active is not [SegmentModelVersion inUse])
        {
            throw new InvalidOperationException("Exactly one version is in use.");
        }

        if (change.To is not int number)
        {
            return inUse.ByHand
                ? Changed(inUse.Chosen(false, null))
                : Changed([], inUse);
        }

        return kept.FirstOrDefault(version => version.Number == number) is { } to
            ? Moved(inUse, to, change, at)
            : null;
    }

    private static SegmentVersionChanged Moved(SegmentModelVersion inUse, SegmentModelVersion to, SegmentVersionChange change, DateTime at)
    {
        if (!change.ByHand)
        {
            Learned(inUse, to, change);
        }

        if (to.Number == inUse.Number)
        {
            return inUse.ByHand ? Changed([], inUse) : Changed(inUse.Chosen(true, at));
        }

        SegmentModelVersion left = inUse.Restated(SegmentModelVersionState.Retired, inUse.AdoptedAt, at, change.Rollback is null ? null : at, change.Rollback);
        SegmentModelVersion put = to.Restated(SegmentModelVersionState.Active, at, null, null, null, change.ByHand, change.ByHand ? at : null);

        return new SegmentVersionChanged([left, put], put);
    }

    private static void Learned(SegmentModelVersion inUse, SegmentModelVersion to, SegmentVersionChange change)
    {
        if (inUse.ByHand)
        {
            throw new InvalidOperationException("A version chosen by hand is moved only by another choice by hand.");
        }

        SegmentModelVersionState from = change.Rollback is null ? SegmentModelVersionState.Candidate : SegmentModelVersionState.Retired;

        if (to.State != from)
        {
            throw new InvalidOperationException("Learning puts only a candidate in use, and goes back only to a version used before.");
        }
    }

    private static SegmentVersionChanged Changed(SegmentModelVersion moved) => new([moved], moved);

    private static SegmentVersionChanged Changed(IReadOnlyList<SegmentModelVersion> written, SegmentModelVersion inUse) => new(written, inUse);

    private static string? RollbackFault(DateTime? rolledBack, SegmentRollback? rollback)
        => (rolledBack is null) != (rollback is null)
            ? "A version rolled back from keeps what the rollback measured, and only then."
            : null;

    private static string? Fault(
        SegmentModelVersionState state,
        bool byHand,
        DateTime trained,
        DateTime? adopted,
        DateTime? retired,
        DateTime? chosen,
        DateTime? rolledBack)
    {
        if ((adopted is null) != (state is SegmentModelVersionState.Candidate))
        {
            return "A version has been put in use unless it is a candidate.";
        }

        if ((retired is not null) != (state is SegmentModelVersionState.Retired))
        {
            return "A version has been taken out of use exactly when it is one used before.";
        }

        if (adopted < trained || retired < adopted)
        {
            return "A version is put in use no earlier than it was made, and taken out of use no earlier than it was put in use.";
        }

        if (byHand && (state is not SegmentModelVersionState.Active || chosen is null))
        {
            return "Only the version in use is chosen by hand, and one chosen by hand says when.";
        }

        return rolledBack is not null && state is not SegmentModelVersionState.Retired
            ? "Only a version used before has been rolled back from."
            : null;
    }

    private SegmentModelVersion Chosen(bool byHand, DateTime? at)
        => Restated(State, AdoptedAt, RetiredAt, RolledBackAt, Rollback, byHand, at);

    private SegmentModelVersion Restated(
        SegmentModelVersionState state,
        DateTime? adoptedAt,
        DateTime? retiredAt,
        DateTime? rolledBackAt,
        SegmentRollback? rollback,
        bool byHand = false,
        DateTime? chosenByHandAt = null)
        => Rehydrate(Number, state, byHand, Model, TrainedAt, adoptedAt, retiredAt, chosenByHandAt, rolledBackAt, Evaluation, rollback, BuiltIn);
}

public sealed record SegmentVersionChange
{
    private SegmentVersionChange(int? to, bool byHand, SegmentRollback? rollback)
    {
        To = to;
        ByHand = byHand;
        Rollback = rollback;
    }

    public static SegmentVersionChange LeftToLearning { get; } = new(null, false, null);

    public int? To { get; }

    public bool ByHand { get; }

    public SegmentRollback? Rollback { get; }

    public static SegmentVersionChange Adopting(int candidate) => new(candidate, false, null);

    public static SegmentVersionChange GoingBack(SegmentRollback rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);

        return new SegmentVersionChange(rollback.BackTo, false, rollback);
    }

    public static SegmentVersionChange ChosenByHand(int number) => new(number, true, null);
}

public sealed record SegmentVersionChanged(IReadOnlyList<SegmentModelVersion> Written, SegmentModelVersion Active);

public sealed record SegmentBuiltInKept(SegmentModelVersion Added, IReadOnlyList<SegmentModelVersion> Moved, SegmentModelVersion Active);
