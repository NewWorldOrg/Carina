using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public sealed record RecordingStartFailure
{
    private RecordingStartFailure(RecordingFault fault, TuneFailureKind? tuneFailure)
    {
        Fault = fault;
        TuneFailure = tuneFailure;
    }

    public static RecordingStartFailure NoTunerWasFree { get; } = new(RecordingFault.TunerContended, null);

    public RecordingFault Fault { get; }

    public TuneFailureKind? TuneFailure { get; }

    public ReservationOutcomeKind Kind
        => Fault is RecordingFault.TuneFailed
            ? ReservationOutcomeKind.TuneFailure
            : ReservationOutcomeKind.Competing;

    public bool IsWorthReportingToTheTuner
        => TuneFailure is TuneFailureKind.NoLock or TuneFailureKind.NoData;

    public static RecordingStartFailure TheTunerWouldNotTune(TuneFailureKind tuneFailure)
        => Enum.IsDefined(tuneFailure)
            ? new RecordingStartFailure(RecordingFault.TuneFailed, tuneFailure)
            : throw new ArgumentOutOfRangeException(
                nameof(tuneFailure),
                tuneFailure,
                "A tune failure is one of the four kinds.");
}
