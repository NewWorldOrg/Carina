using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed class FollowedRecording(RecordingId id, string source, ServiceId service, ExtractionVersion version)
{
    private const int StillRecording = 0;

    private const int HasEndedMark = 1;

    private const int HasGoneMark = 2;

    private int told;

    public RecordingId Id { get; } = id ?? throw new ArgumentNullException(nameof(id));

    public string Source { get; } = string.IsNullOrEmpty(source) ? throw new ArgumentException("A followed recording is read from a file.", nameof(source)) : source;

    public ServiceId Service { get; } = service ?? throw new ArgumentNullException(nameof(service));

    public ExtractionVersion Version { get; } = version ?? throw new ArgumentNullException(nameof(version));

    public LearningExtractionState Held { get; private init; } = LearningExtractionState.Following;

    public bool HasEnded => Volatile.Read(ref told) is HasEndedMark;

    public bool HasGone => Volatile.Read(ref told) is HasGoneMark;

    public static FollowedRecording Recorded(RecordingId id, string source, ServiceId service, ExtractionVersion version)
    {
        FollowedRecording recorded = new(id, source, service, version) { Held = LearningExtractionState.Reading };

        recorded.Ended();

        return recorded;
    }

    public void Ended() => Interlocked.CompareExchange(ref told, HasEndedMark, StillRecording);

    public void Went() => Volatile.Write(ref told, HasGoneMark);
}

public interface ILearningFollower
{
    Task FollowAsync(FollowedRecording recording, CancellationToken cancellationToken);
}
