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
/// One version of the model segments are detected with, kept for good: its number, counted from
/// <see cref="FirstNumber"/>; where it stands; whether it was chosen by hand, which only the version in use
/// can be; its parameters and the number of kinds they tell apart; when it was made, put in use, chosen by
/// hand, taken out of use and rolled back from. Only a candidate has never been put in use, and only a
/// version used before has been taken out of use or rolled back from. Exactly one version is in use at a
/// time, which the store holds; the first holds <see cref="SegmentModel.First"/>.
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

    public SegmentModel Model => model ??= new SegmentModel(Weights, StationOffsets, SeriesOffsets, Thresholds, Standardisation);

    public static SegmentModelVersion Rehydrate(
        int number,
        SegmentModelVersionState state,
        bool byHand,
        SegmentModel model,
        DateTime trainedAt,
        DateTime? adoptedAt,
        DateTime? retiredAt,
        DateTime? chosenByHandAt,
        DateTime? rolledBackAt)
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
        string? fault = Fault(state, byHand, trained, adopted, retired, chosen, rolledBack);

        if (fault is not null)
        {
            throw new ArgumentException(fault);
        }

        return new SegmentModelVersion
        {
            Number = number,
            State = state,
            ByHand = byHand,
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
            model = model,
        };
    }

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
}
