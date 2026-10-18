using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The detections held in memory, one per recording and version, a detection kept taking the place of the one
/// held for the same pair.
/// </summary>
public sealed class HeldRecordingDetections : IRecordingDetectionRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<(RecordingId Recording, int Version), RecordingDetection> held = [];

    public int Keeps { get; private set; }

    public IReadOnlyList<RecordingDetection> All()
    {
        lock (gate)
        {
            return [.. held.Values];
        }
    }

    public RecordingDetection? Of(RecordingId recording, int modelVersion)
    {
        lock (gate)
        {
            return held.GetValueOrDefault((recording, modelVersion));
        }
    }

    public Task<RecordingDetection?> FindAsync(RecordingId recording, int modelVersion, CancellationToken cancellationToken)
        => Task.FromResult(Of(recording, modelVersion));

    public Task<IReadOnlyList<RecordingDetection>> ListAsync(
        IReadOnlyCollection<RecordingId> recordings,
        int modelVersion,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<RecordingDetection>>(
                [.. held.Values.Where(detection => detection.ModelVersion == modelVersion && recordings.Contains(detection.RecordingId))]);
        }
    }

    public Task KeepAsync(RecordingDetection detection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detection);

        lock (gate)
        {
            held[(detection.RecordingId, detection.ModelVersion)] = detection;
            Keeps++;
        }

        return Task.CompletedTask;
    }
}
