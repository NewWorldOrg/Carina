using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

/// <summary>
/// How far a reservation being recorded is actually promised, and the tuner it is running on, read off the
/// recordings in flight that were made of it.
/// </summary>
public sealed record RecordingHold(DateTime Until, TunerDeviceId? Tuner)
{
    /// <summary>
    /// For each reservation a recording in flight was made of, the latest end any of them is promised, and the tuner
    /// the recording with that end names, or the one an earlier recording named when it names none.
    /// </summary>
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
