using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public sealed record RecordingHold(DateTime Until, TunerDeviceId? Tuner)
{
    public static IReadOnlyDictionary<ReservationId, RecordingHold> Of(IReadOnlyList<Recording> inFlight)
    {
        ArgumentNullException.ThrowIfNull(inFlight);

        Dictionary<ReservationId, RecordingHold> held = [];

        foreach (Recording recording in inFlight)
        {
            if (recording.ReservationId is not { } reservation)
            {
                continue;
            }

            if (!held.TryGetValue(reservation, out RecordingHold? standing)
                || recording.ExpectedWindowEnd > standing.Until)
            {
                held[reservation] = new RecordingHold(
                    recording.ExpectedWindowEnd,
                    recording.TunerDeviceId ?? standing?.Tuner);
            }
        }

        return held;
    }
}
