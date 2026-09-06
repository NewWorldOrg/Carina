using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed class EncodeStandingBoard
{
    public static readonly EncodeStandingBoard Empty = new(new Dictionary<RecordingId, EncodeStanding>());

    private readonly IReadOnlyDictionary<RecordingId, EncodeStanding> standings;

    private EncodeStandingBoard(IReadOnlyDictionary<RecordingId, EncodeStanding> standings)
        => this.standings = standings;

    public static EncodeStandingBoard Of(IEnumerable<(RecordingId Recording, EncodeJobStatus Status)> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        Dictionary<RecordingId, List<EncodeJobStatus>> gathered = [];

        foreach ((RecordingId recording, EncodeJobStatus status) in held)
        {
            ArgumentNullException.ThrowIfNull(recording);

            if (!gathered.TryGetValue(recording, out List<EncodeJobStatus>? statuses))
            {
                statuses = [];
                gathered[recording] = statuses;
            }

            statuses.Add(status);
        }

        return new EncodeStandingBoard(
            gathered.ToDictionary(entry => entry.Key, entry => EncodeStandings.Over(entry.Value)));
    }

    public EncodeStanding For(RecordingId recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return standings.TryGetValue(recording, out EncodeStanding standing)
            ? standing
            : EncodeStanding.NotEncoded;
    }
}
