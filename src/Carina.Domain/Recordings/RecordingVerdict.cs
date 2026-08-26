using System.Globalization;

namespace Carina.Domain.Recordings;

public sealed record RecordingVerdict
{
    private RecordingVerdict(RecordingOutcome outcome, double coverage, IReadOnlyList<RecordingFault> faults)
    {
        Outcome = outcome;
        Coverage = coverage;
        Faults = faults;
    }

    public RecordingOutcome Outcome { get; }

    public double Coverage { get; }

    public IReadOnlyList<RecordingFault> Faults { get; }

    public static RecordingVerdict Of(
        RecordingOutcome outcome,
        double coverage,
        IReadOnlyList<RecordingFault> faults)
    {
        ArgumentNullException.ThrowIfNull(faults);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A recording ends in one of three ways.");
        }

        if (outcome is not RecordingOutcome.Complete && faults.Count is 0)
        {
            throw new ArgumentException(
                $"A recording that ended {outcome} says why, in the classes the ledger holds.",
                nameof(faults));
        }

        return new RecordingVerdict(outcome, coverage, [.. faults]);
    }

    public bool Names(RecordingFault fault) => Faults.Contains(fault);

    public IReadOnlyList<OutcomeDetail> Detail(DateTime noticedAt)
        => [.. Faults.Select(fault => new OutcomeDetail(fault, null, Measured(), noticedAt))];

    private string Measured()
        => string.Create(CultureInfo.InvariantCulture, $"covered {Coverage:F4} of the window");
}
