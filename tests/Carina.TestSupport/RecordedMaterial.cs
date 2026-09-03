using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.TestSupport;

public sealed record RecordedPair(Recording Original, Recording Encoded);

public sealed class RecordedMaterial(DirectoryInfo mounted, OutputRoot root)
{
    public const int SomeNetworkId = 50001;

    public const int SomeServiceId = 1040;

    public const int SomeEventId = 4001;

    public const string TransportStream = ".m2ts";

    public const string Encoded = ".mp4";

    public static readonly DateTime Noon = new(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Ran = TimeSpan.FromMinutes(30);

    public OutputRoot Root { get; } = root;

    public Recording Ended(RecordingId id, string source, string extension = TransportStream, int serviceId = SomeServiceId)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extension);

        RecordingFileName name = RecordingFileName.For(id, extension);
        string placed = Path.Combine(mounted.FullName, name.Value);

        File.Copy(source, placed, overwrite: true);

        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(SomeNetworkId), new ServiceId(serviceId), new EventId(SomeEventId), Noon),
            Root,
            name,
            Noon,
            Noon + Ran,
            new ProgrammeSnapshot("A synthetic programme", "What the generator drew", string.Empty, [], Noon),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("synthetic-0"));

        recording.Wrote(Ran);
        recording.Abort(Noon + Ran);
        recording.Settle(RecordingOutcome.Complete, new FileInfo(placed).Length, Noon + Ran);

        return recording;
    }

    public RecordedPair Pair(RecordingId original, RecordingId encoded, string transportStream, string h264)
        => new(
            Ended(original, transportStream, TransportStream),
            Ended(encoded, h264, Encoded));
}
