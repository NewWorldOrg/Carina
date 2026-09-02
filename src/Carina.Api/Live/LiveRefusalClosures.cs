using System.Net.WebSockets;

using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveRefusalClosures
{
    public static WebSocketCloseStatus Status(LiveRefusal refusal)
        => refusal switch
        {
            LiveRefusal.NoSuchChannel => WebSocketCloseStatus.InvalidPayloadData,
            LiveRefusal.NoTunerFree => WebSocketCloseStatus.PolicyViolation,
            LiveRefusal.TooManyAlready => WebSocketCloseStatus.PolicyViolation,
            LiveRefusal.WouldNotTune => WebSocketCloseStatus.InternalServerError,
            LiveRefusal.DriverUnavailable => WebSocketCloseStatus.InternalServerError,
            LiveRefusal.TranscoderWouldNotStart => WebSocketCloseStatus.InternalServerError,
            _ => throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A viewer is refused for one of the reasons named here."),
        };

    public static string Because(LiveRefusal refusal)
        => refusal switch
        {
            LiveRefusal.NoSuchChannel => "No channel by that network and service is held here.",
            LiveRefusal.NoTunerFree => "Every tuner that receives this channel is busy.",
            LiveRefusal.TooManyAlready => "As many transcoders are running as this machine is asked to.",
            LiveRefusal.WouldNotTune => "The tuner would not lock on to this channel.",
            LiveRefusal.DriverUnavailable => "Nothing on this app supplies a transport stream to live viewing.",
            LiveRefusal.TranscoderWouldNotStart => "The transcoder this channel needs would not start.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A viewer is refused for one of the reasons named here."),
        };
}
