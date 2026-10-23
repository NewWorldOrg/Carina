using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// Where a version of the model stands: made by learning and not put in use, the one in use, or one used
/// before.
/// </summary>
public enum SegmentModelVersionState
{
    Candidate = 1,

    Active = 2,

    Retired = 3,
}

/// <summary>
/// One version of the model segments are detected with: its number, counted from <see cref="FirstNumber"/>;
/// where it stands; whether it was chosen by hand, which only the version in use can be; whether it is built in,
/// made by hand and shipped with a release rather than learned, which is never evaluated; its parameters and the
/// number of kinds they tell apart; how it did when it was evaluated, if it was; when it was made, put in use,
/// chosen by hand, taken out of use and rolled back from, and what the rollback measured. Only a candidate has
/// never been put in use, and only a version used before has been taken out of use or rolled back from. Exactly
/// one version is in use at a time, which the store holds. The built-in version of a release
/// (<see cref="SegmentModel.BuiltIn"/>) is kept through <see cref="KeepBuiltIn"/>; the version in use changes
/// only through <see cref="Change"/> and that.
/// </summary>
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

    /// <summary>
    /// The candidate learning made at <paramref name="trainedAt"/> with <paramref name="model"/>, numbered
    /// <paramref name="number"/>, as <paramref name="evaluation"/> judged it.
    /// </summary>
    public static SegmentModelVersion Learned(int number, SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return Rehydrate(number, SegmentModelVersionState.Candidate, false, model, trainedAt, null, null, null, null, evaluation);
    }

    /// <summary>
    /// What keeping <paramref name="builtIn"/>, the built-in version of a release, at <paramref name="at"/> among the
    /// versions <paramref name="kept"/> writes; nothing when a built-in version already holds the same parameters,
    /// whatever its state. Otherwise the version holding it is numbered after the highest and made at
    /// <paramref name="at"/>. With no version in use it is put in use. It takes the place of the version in use when
    /// that one is built in and not chosen by hand, which is taken out of use; beside a version chosen by hand or
    /// learned it is kept as a candidate, and the version in use stays.
    /// </summary>
    public static SegmentBuiltInKept? KeepBuiltIn(IReadOnlyList<SegmentModelVersion> kept, SegmentModel builtIn, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(builtIn);

        if (kept.Any(version => version.BuiltIn && version.Model.Equals(builtIn)))
        {
            return null;
        }

        int number = kept.Count is 0 ? FirstNumber : kept.Max(version => version.Number) + 1;

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

    /// <summary>
    /// The versions <paramref name="change"/> moves when it is made at <paramref name="at"/> to the versions
    /// <paramref name="kept"/>, the one leaving use first, and the version in use after it; none when it names
    /// a number no version has. A version chosen by hand is put in use, or only marked chosen when it already
    /// is; the one it takes the place of is taken out of use and no longer chosen. Leaving the choice to
    /// learning keeps the version in use and no longer marks it chosen. Learning puts only a candidate in use
    /// and goes back only to a version used before, and never moves a version chosen by hand; a rollback is
    /// kept on the version it leaves. A version put in use again sheds what an earlier rollback from it
    /// measured. A change that moves nothing writes nothing.
    /// </summary>
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

/// <summary>
/// A change to which version of the model is in use, or to how it came to be: learning putting a candidate in
/// use, a rollback going back to the version before, a version chosen by hand, or the choice left to learning
/// again.
/// <see cref="To"/> is the version moved to, none when the version in use stays or is worked out from the
/// versions kept.
/// </summary>
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

/// <summary>
/// What a change to the version in use moved: the versions to write, the one leaving use first, and the version
/// in use after it.
/// </summary>
public sealed record SegmentVersionChanged(IReadOnlyList<SegmentModelVersion> Written, SegmentModelVersion Active);

/// <summary>
/// What keeping the built-in version of a release wrote: the version added, as it stands; the version it took the
/// place of in use, if it did, to be written before it; and the version in use after it.
/// </summary>
public sealed record SegmentBuiltInKept(SegmentModelVersion Added, IReadOnlyList<SegmentModelVersion> Moved, SegmentModelVersion Active);
