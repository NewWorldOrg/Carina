using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// A station's watermark as it was learned from one recording of one service, kept so that the
/// recordings of that service which are read after it are judged by a mark learned ahead of them
///. It names the recording it was learned from, and it never judges that recording:
/// the first recording of a service is judged with no watermark at all rather than with the one
/// learned from itself.
/// </summary>
public sealed class StationWatermark
{
    public const int KeptPerService = 2;

    private StationWatermark()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public RecordingId LearnedFrom { get; private set; } = null!;

    public DateTime LearnedAt { get; private set; }

    public byte[] Pattern { get; private set; } = [];

    public WatermarkMask Mask => WatermarkMask.Unpacked(Pattern);

    public static StationWatermark Learn(
        NetworkId networkId,
        ServiceId serviceId,
        RecordingId learnedFrom,
        WatermarkMask learned,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(learned);

        return Rehydrate(networkId, serviceId, learnedFrom, at, learned.Packed());
    }

    public static StationWatermark Rehydrate(
        NetworkId networkId,
        ServiceId serviceId,
        RecordingId learnedFrom,
        DateTime learnedAt,
        byte[] pattern)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(learnedFrom);
        ArgumentNullException.ThrowIfNull(pattern);

        WatermarkMask checkedPattern = WatermarkMask.Unpacked(pattern);

        return new StationWatermark
        {
            NetworkId = networkId,
            ServiceId = serviceId,
            LearnedFrom = learnedFrom,
            LearnedAt = UtcTimes.Required(learnedAt, nameof(learnedAt)),
            Pattern = checkedPattern.Packed(),
        };
    }

    public bool MayJudge(NetworkId networkId, ServiceId serviceId, RecordingId judged)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(judged);

        return NetworkId.Equals(networkId) && ServiceId.Equals(serviceId) && !LearnedFrom.Equals(judged);
    }
}
